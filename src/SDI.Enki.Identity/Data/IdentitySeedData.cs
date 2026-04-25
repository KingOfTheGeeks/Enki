using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
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
    /// <summary>
    /// Convenience re-export of <see cref="AuthConstants.WebApiScope"/>
    /// for callers that already lived against this class. The string
    /// value is canonical in <see cref="AuthConstants"/>.
    /// </summary>
    public const string WebApiScope      = AuthConstants.WebApiScope;
    public const string BlazorClientId   = "enki-blazor";
    public const string BlazorClientName = "Enki Blazor Server";

    private const string DevFallbackUserPassword     = "Enki!dev1";
    private const string DevFallbackBlazorClientSecret = "enki-blazor-dev-secret";

    /// <summary>Re-export. Canonical home is <see cref="AuthConstants.EnkiAdminRole"/>.</summary>
    public const string EnkiAdminRole = AuthConstants.EnkiAdminRole;

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
            // Two-phase idempotency:
            //   1. If the user doesn't exist, create + add the baseline claims.
            //   2. In either branch, reconcile the IsEnkiAdmin flag so flipping
            //      the seed tuple takes effect on the next boot. Existing
            //      user's SecurityStamp is rotated on role change so any live
            //      refresh tokens are invalidated and the next sign-in issues
            //      a fresh role claim.
            var idString = seed.IdentityId.ToString();
            var user     = await userMgr.FindByIdAsync(idString);
            var creating = user is null;

            if (creating)
            {
                user = new ApplicationUser
                {
                    Id                 = idString,
                    UserType           = "Team",
                    UserName           = seed.Username,
                    NormalizedUserName = seed.Username.ToUpperInvariant(),
                    Email              = seed.Email,
                    NormalizedEmail    = seed.Email.ToUpperInvariant(),
                    EmailConfirmed     = true,
                    LockoutEnabled     = true,
                    IsEnkiAdmin        = seed.IsEnkiAdmin,
                    SecurityStamp      = Guid.NewGuid().ToString(),
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
            await ReconcileAdminRoleAsync(userMgr, user!, seed.IsEnkiAdmin);
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
    /// Converges the user's stored role claim with the <c>isAdmin</c> flag
    /// from the seed tuple + the <see cref="ApplicationUser.IsEnkiAdmin"/>
    /// column. Adds the <c>role=enki-admin</c> claim if missing, removes
    /// it if present on a non-admin, and rotates the SecurityStamp on any
    /// change so outstanding refresh tokens no longer mint stale claims.
    /// </summary>
    private static async Task ReconcileAdminRoleAsync(
        UserManager<ApplicationUser> userMgr,
        ApplicationUser user,
        bool desiredAdmin)
    {
        var claims = await userMgr.GetClaimsAsync(user);
        var existing = claims.FirstOrDefault(c =>
            c.Type == "role" && c.Value == EnkiAdminRole);

        var hasAdminClaim   = existing is not null;
        var hasAdminColumn  = user.IsEnkiAdmin;
        var needsClaimFlip  = hasAdminClaim != desiredAdmin;
        var needsColumnFlip = hasAdminColumn != desiredAdmin;

        if (!needsClaimFlip && !needsColumnFlip) return;

        if (needsClaimFlip)
        {
            if (desiredAdmin)
                await userMgr.AddClaimAsync(user, new System.Security.Claims.Claim("role", EnkiAdminRole));
            else
                await userMgr.RemoveClaimAsync(user, existing!);
        }

        if (needsColumnFlip)
        {
            user.IsEnkiAdmin = desiredAdmin;
            await userMgr.UpdateAsync(user);
        }

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

        if (await scopeMgr.FindByNameAsync(WebApiScope) is null)
        {
            await scopeMgr.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name        = WebApiScope,
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
                    Permissions.Prefixes.Scope + WebApiScope,
                },
            });
        }
        // Existing client: deliberately untouched. Operations needing a
        // change must delete + recreate the row (or use an admin tool
        // when one exists) — never silently overwriting a possibly-
        // rotated secret.
    }
}
