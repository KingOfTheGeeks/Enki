using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using SDI.Enki.Identity.Validation;
using SDI.Enki.Shared.Identity;
using SDI.Enki.Shared.Seeding;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SDI.Enki.Identity.Data;

/// <summary>
/// Seeds the Enki Identity server with its canonical users, the WebApi
/// resource scope, and the Blazor client registration. Idempotent — runs at
/// startup and skips rows that already exist. This is the OpenIddict +
/// ASP.NET Identity equivalent of legacy Athena's Identity seed.
///
/// <para>
/// <b>Credential safety:</b> the default user password and Blazor client
/// secret come from configuration (<c>Identity:Seed:DefaultUserPassword</c>
/// and <c>Identity:Seed:BlazorClientSecret</c>). When the host runs under
/// <c>ASPNETCORE_ENVIRONMENT=Development</c> a fallback to known dev
/// values is allowed; in any other environment a missing config value
/// throws — the host fails to start rather than silently seeding
/// well-known credentials into a production database.
/// </para>
///
/// <para>
/// On an existing Blazor OIDC client (the <c>BlazorClientId</c> row
/// present in OpenIddict tables), <see cref="SeedAsync"/> does NOT
/// overwrite the client secret on every boot. The earlier upsert path
/// would reset rotated secrets back to the dev value at every restart.
/// Operations that genuinely need to change a client (new redirect URI,
/// new permission) must delete the application row first or use an
/// admin tool when one exists.
/// </para>
/// </summary>
public static class IdentitySeedData
{
    public const string BlazorClientId   = "enki-blazor";
    public const string BlazorClientName = "Enki Blazor Server";

    private const string DevFallbackUserPassword     = "Enki!dev1";
    private const string DevFallbackBlazorClientSecret = "enki-blazor-dev-secret";

    /// <summary>
    /// Apply at host startup. Idempotent.
    /// User identities come from <see cref="SeedUsers.All"/> — same
    /// canonical roster the master-DB seed reads.
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope    = services.CreateScope();
        var sp             = scope.ServiceProvider;
        var userMgr        = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration  = sp.GetRequiredService<IConfiguration>();
        var environment    = sp.GetRequiredService<IHostEnvironment>();

        var defaultPassword     = ResolveCredential(configuration, environment,
                                      "Identity:Seed:DefaultUserPassword",     DevFallbackUserPassword,
                                      humanName: "default user password");
        var blazorClientSecret  = ResolveCredential(configuration, environment,
                                      "Identity:Seed:BlazorClientSecret",      DevFallbackBlazorClientSecret,
                                      humanName: "Blazor OIDC client secret");

        foreach (var seed in SeedUsers.All)
        {
            // Validate the classification triplet up-front — same validator
            // the admin endpoints use, so a malformed seed entry fails the
            // host boot rather than landing an invalid AspNetUsers row that
            // the admin UI would later refuse to edit.
            var seedFailures = UserClassificationValidator.Validate(
                userTypeName:    seed.UserType,
                teamSubtypeName: seed.TeamSubtype,
                tenantId:        seed.TenantId,
                isEnkiAdmin:     seed.IsEnkiAdmin);
            if (seedFailures.Count > 0)
                throw new InvalidOperationException(
                    $"SeedUser '{seed.Username}' has an invalid classification: " +
                    string.Join("; ", seedFailures.Select(f => $"{f.Field}: {f.Message}")));

            // Two-phase idempotency:
            //   1. If the user doesn't exist, create + add the baseline profile claims.
            //   2. In either branch, reconcile the IsEnkiAdmin column so flipping
            //      the seed tuple takes effect on the next boot. The role claim is
            //      derived from the column at sign-in by EnkiUserClaimsPrincipalFactory
            //      — never persisted as an AspNetUserClaims row.
            var idString = seed.IdentityId.ToString();
            var user     = await userMgr.FindByIdAsync(idString);
            var creating = user is null;

            if (creating)
            {
                user = new ApplicationUser
                {
                    Id                       = idString,
                    UserType                 = SDI.Enki.Shared.Identity.UserType.FromName(seed.UserType),
                    UserName                 = seed.Username,
                    NormalizedUserName       = seed.Username.ToUpperInvariant(),
                    Email                    = seed.Email,
                    NormalizedEmail          = seed.Email.ToUpperInvariant(),
                    EmailConfirmed           = true,
                    LockoutEnabled           = true,
                    IsEnkiAdmin              = seed.IsEnkiAdmin,
                    SecurityStamp            = Guid.NewGuid().ToString(),
                    SessionLifetimeMinutes   = seed.SessionLifetimeMinutes,
                    SessionLifetimeUpdatedAt = seed.SessionLifetimeMinutes is null ? null : DateTimeOffset.UtcNow,
                    SessionLifetimeUpdatedBy = seed.SessionLifetimeMinutes is null ? null : "seed",
                    TeamSubtype              = seed.TeamSubtype,
                    TenantId                 = seed.TenantId,
                };

                var result = await userMgr.CreateAsync(user, defaultPassword);
                if (!result.Succeeded)
                    throw new InvalidOperationException(
                        $"Failed to seed user '{seed.Username}': " +
                        string.Join("; ", result.Errors.Select(e => e.Description)));

                await userMgr.AddClaimsAsync(user, new[]
                {
                    new System.Security.Claims.Claim("name",        $"{seed.FirstName} {seed.LastName}"),
                    new System.Security.Claims.Claim("given_name",  seed.FirstName),
                    new System.Security.Claims.Claim("family_name", seed.LastName),
                });
            }

            // Reconcile the admin bit for both newly-created and existing users.
            await ReconcileAdminColumnAsync(userMgr, user!, seed.IsEnkiAdmin);

            // Reconcile the session-lifetime override too, so flipping the
            // value in SeedUsers takes effect on the next host boot without
            // a DB reset. Same stamp-rotation rationale as the admin bit:
            // any in-flight refresh token issued under the old window must
            // stop validating once the policy changes.
            await ReconcileSessionLifetimeAsync(userMgr, user!, seed.SessionLifetimeMinutes);

            // Reconcile classification (TeamSubtype + TenantId). UserType
            // itself is immutable post-creation per the design — if a seed
            // entry's UserType disagrees with the existing column we throw
            // rather than silently mutating; the operator must drop the
            // user and re-seed if they truly meant to switch buckets.
            await ReconcileClassificationAsync(userMgr, user!, seed);

            // Reconcile capability claims — diff existing rows against
            // seed.Capabilities and add/remove to converge. Stamp rotates
            // on any change so the new claim set takes effect on the
            // next exchange.
            await ReconcileCapabilitiesAsync(userMgr, user!, seed.Capabilities);
        }

        await SeedOpenIddictAsync(sp, blazorClientSecret);
    }

    /// <summary>
    /// Pulls a credential from configuration. In Development, falls back
    /// to a known dev value if the config key is unset. In any other
    /// environment, throws — startup fails rather than silently using a
    /// well-known credential against a real database.
    /// </summary>
    private static string ResolveCredential(
        IConfiguration  config,
        IHostEnvironment env,
        string          configKey,
        string          devFallback,
        string          humanName)
    {
        var value = config[configKey];
        if (!string.IsNullOrEmpty(value)) return value;

        if (env.IsDevelopment()) return devFallback;

        throw new InvalidOperationException(
            $"Missing required configuration value '{configKey}' " +
            $"for the {humanName}. Set it via appsettings, environment variable, " +
            $"or your secret store. Refusing to fall back to the dev default in " +
            $"environment '{env.EnvironmentName}'.");
    }

    /// <summary>
    /// Converges the user's <see cref="ApplicationUser.IsEnkiAdmin"/>
    /// column with the desired flag from the seed tuple. The role claim
    /// is derived from this column at sign-in by
    /// <see cref="EnkiUserClaimsPrincipalFactory"/>, so the seeder never
    /// touches AspNetUserClaims rows for the role.
    ///
    /// <para>
    /// On any column flip the SecurityStamp is rotated so existing
    /// refresh tokens stop validating and the next sign-in re-runs the
    /// claims factory against the new column value.
    /// </para>
    /// </summary>
    private static async Task ReconcileAdminColumnAsync(
        UserManager<ApplicationUser> userMgr,
        ApplicationUser user,
        bool desiredAdmin)
    {
        if (user.IsEnkiAdmin == desiredAdmin) return;

        user.IsEnkiAdmin = desiredAdmin;
        await userMgr.UpdateAsync(user);
        await userMgr.UpdateSecurityStampAsync(user);
    }

    /// <summary>
    /// Converges <see cref="ApplicationUser.SessionLifetimeMinutes"/> with
    /// the desired value from the seed tuple. Stamps the metadata columns
    /// + rotates the security stamp on any change so the new lifetime
    /// takes effect on the next exchange. No-ops when the column already
    /// matches.
    /// </summary>
    private static async Task ReconcileSessionLifetimeAsync(
        UserManager<ApplicationUser> userMgr,
        ApplicationUser user,
        int? desiredMinutes)
    {
        if (user.SessionLifetimeMinutes == desiredMinutes) return;

        user.SessionLifetimeMinutes   = desiredMinutes;
        user.SessionLifetimeUpdatedAt = DateTimeOffset.UtcNow;
        user.SessionLifetimeUpdatedBy = "seed";

        await userMgr.UpdateAsync(user);
        await userMgr.UpdateSecurityStampAsync(user);
    }

    /// <summary>
    /// Converges classification fields (<c>TeamSubtype</c> + <c>TenantId</c>)
    /// from the seed tuple. <c>UserType</c> mismatches throw — the design
    /// forbids switching Team↔Tenant on an existing row; the operator
    /// must drop the row and reseed if they truly meant the change.
    /// Mutable changes rotate the security stamp so a downgraded user
    /// gets force-resigned at the next refresh.
    /// </summary>
    private static async Task ReconcileClassificationAsync(
        UserManager<ApplicationUser> userMgr,
        ApplicationUser user,
        SeedUser seed)
    {
        var seedUserType = SDI.Enki.Shared.Identity.UserType.FromName(seed.UserType);
        if (user.UserType is not null && user.UserType != seedUserType)
            throw new InvalidOperationException(
                $"SeedUser '{seed.Username}' UserType is '{seed.UserType}' but the existing " +
                $"row carries '{user.UserType.Name}'. UserType is immutable after creation; " +
                $"drop the row in AspNetUsers and reseed if the change is intentional.");

        var changed = false;

        if (user.UserType is null)
        {
            user.UserType = seedUserType;
            changed = true;
        }
        if (!string.Equals(user.TeamSubtype, seed.TeamSubtype, StringComparison.Ordinal))
        {
            user.TeamSubtype = seed.TeamSubtype;
            changed = true;
        }
        if (user.TenantId != seed.TenantId)
        {
            user.TenantId = seed.TenantId;
            changed = true;
        }

        if (!changed) return;

        await userMgr.UpdateAsync(user);
        await userMgr.UpdateSecurityStampAsync(user);
    }

    /// <summary>
    /// Converges <c>enki:capability</c> claims with the seed entry's
    /// list. Filters to <see cref="EnkiCapabilities.IsKnown"/> entries
    /// so a typo in seed data doesn't smuggle a dead claim onto a user
    /// (admin grants go through a similar validator). Tenant users with
    /// any capability listed throw — capabilities are Team-side only and
    /// the validator already rejects the combination at every other site.
    /// </summary>
    private static async Task ReconcileCapabilitiesAsync(
        UserManager<ApplicationUser> userMgr,
        ApplicationUser user,
        IReadOnlyList<string>? desiredCapabilities)
    {
        var desired = (desiredCapabilities ?? Array.Empty<string>())
            .Where(EnkiCapabilities.IsKnown)
            .Distinct()
            .ToHashSet();

        if (user.UserType == SDI.Enki.Shared.Identity.UserType.Tenant && desired.Count > 0)
            throw new InvalidOperationException(
                $"SeedUser '{user.UserName}' is Tenant-type but lists capabilities " +
                $"{{{string.Join(", ", desired)}}}. Capabilities are Team-side only.");

        var existing = (await userMgr.GetClaimsAsync(user))
            .Where(c => c.Type == EnkiClaimTypes.Capability)
            .ToList();
        var existingValues = existing.Select(c => c.Value).ToHashSet();

        var toAdd    = desired.Except(existingValues).ToList();
        var toRemove = existing.Where(c => !desired.Contains(c.Value)).ToList();

        if (toAdd.Count == 0 && toRemove.Count == 0) return;

        foreach (var c in toRemove)
            await userMgr.RemoveClaimAsync(user, c);
        foreach (var v in toAdd)
            await userMgr.AddClaimAsync(user, new System.Security.Claims.Claim(EnkiClaimTypes.Capability, v));

        await userMgr.UpdateSecurityStampAsync(user);
    }

    /// <summary>
    /// Registers the WebApi scope and the Blazor Server client. The
    /// scope is straight upsert. The client is **create-only** — once
    /// it exists, this method does NOT touch it again. That preserves
    /// any rotated secret and prevents the boot-time secret reset that
    /// the previous unconditional upsert path caused.
    /// </summary>
    private static async Task SeedOpenIddictAsync(IServiceProvider sp, string blazorClientSecret)
    {
        var scopeMgr  = sp.GetRequiredService<IOpenIddictScopeManager>();
        var clientMgr = sp.GetRequiredService<IOpenIddictApplicationManager>();

        if (await scopeMgr.FindByNameAsync(AuthConstants.WebApiScope) is null)
        {
            await scopeMgr.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name        = AuthConstants.WebApiScope,
                DisplayName = "Enki Web API",
                Description = "Access to Enki tenant + master data endpoints.",
                Resources   = { "resource_server_enki" },
            });
        }

        if (await clientMgr.FindByClientIdAsync(BlazorClientId) is null)
        {
            await clientMgr.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId     = BlazorClientId,
                ClientSecret = blazorClientSecret,
                DisplayName  = BlazorClientName,
                ConsentType  = ConsentTypes.Implicit,
                ClientType   = ClientTypes.Confidential,
                RedirectUris =
                {
                    new Uri("http://localhost:5073/signin-oidc"),
                    new Uri("https://localhost:7109/signin-oidc"),
                },
                PostLogoutRedirectUris =
                {
                    new Uri("http://localhost:5073/signout-callback-oidc"),
                    new Uri("https://localhost:7109/signout-callback-oidc"),
                },
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.Endpoints.EndSession,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code,
                    Permissions.Scopes.Email,
                    Permissions.Scopes.Profile,
                    Permissions.Scopes.Roles,
                    Permissions.Prefixes.Scope + AuthConstants.WebApiScope,
                },
            });
        }
        // Existing client: deliberately untouched. Operations needing a
        // change must delete + recreate the row (or use an admin tool
        // when one exists) — never silently overwriting a possibly-
        // rotated secret.
    }
}
