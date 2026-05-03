using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using SDI.Enki.Identity.Data;
using SDI.Enki.Identity.Validation;
using SDI.Enki.Shared.Identity;
using SDI.Enki.Shared.Seeding;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SDI.Enki.Identity.Bootstrap;

/// <summary>
/// Idempotent OpenIddict + admin-user provisioning. Lifts the seed
/// logic out of the Dev-only host-startup path so the Migrator CLI
/// can drive the same bootstrap during real deploys, with credentials
/// supplied by the deploy pipeline rather than the dev fallback.
///
/// <para>
/// Two entry points cover the two use cases:
/// <see cref="BootstrapForProductionAsync"/> takes one admin's
/// credentials and the Blazor OIDC details, creates a Team-Office
/// Enki-admin user, and ensures the OpenIddict scope + client exist.
/// <see cref="SeedDevRosterAsync"/> walks <see cref="SeedUsers.All"/>
/// for the dev rig, applying the same reconciler the previous
/// startup-gated <c>IdentitySeedData</c> ran.
/// </para>
///
/// <para>
/// Both funnel into <see cref="EnsureOpenIddictAsync"/>. The OIDC
/// client row is <b>create-only</b>: an existing application is
/// never overwritten, so a rotated client secret survives a
/// re-bootstrap. Run the command twice — second run is a no-op.
/// </para>
/// </summary>
public sealed class IdentityBootstrapper(
    UserManager<ApplicationUser> userManager,
    IOpenIddictApplicationManager openIddictAppManager,
    IOpenIddictScopeManager openIddictScopeManager,
    ILogger<IdentityBootstrapper> logger)
{
    /// <summary>OIDC client id the BlazorServer host authenticates with.</summary>
    public const string BlazorClientId   = "enki-blazor";
    public const string BlazorClientName = "Enki Blazor Server";

    /// <summary>
    /// Redirect URIs registered on the OIDC client. Different per
    /// environment — dev uses both localhost http + https; staging /
    /// prod use one HTTPS public URL each.
    /// </summary>
    public sealed record BlazorClientRedirects(
        IReadOnlyList<Uri> RedirectUris,
        IReadOnlyList<Uri> PostLogoutRedirectUris);

    /// <summary>
    /// Redirects for the dev rig: both http and https localhost
    /// targets so a Blazor instance launched via either profile
    /// signs in cleanly.
    /// </summary>
    public static BlazorClientRedirects DevRedirects() => new(
        RedirectUris:
        [
            new Uri("http://localhost:5073/signin-oidc"),
            new Uri("https://localhost:7109/signin-oidc"),
        ],
        PostLogoutRedirectUris:
        [
            new Uri("http://localhost:5073/signout-callback-oidc"),
            new Uri("https://localhost:7109/signout-callback-oidc"),
        ]);

    /// <summary>
    /// Derive redirects from the BlazorServer host's public base URI
    /// (e.g. <c>https://dev.sdiamr.com/</c>). Trailing slash recommended
    /// — <see cref="Uri"/> resolves the relative paths cleanly either way.
    /// </summary>
    public static BlazorClientRedirects FromBlazorBaseUri(Uri blazorBaseUri)
    {
        ArgumentNullException.ThrowIfNull(blazorBaseUri);

        return new BlazorClientRedirects(
            RedirectUris:           [new Uri(blazorBaseUri, "signin-oidc")],
            PostLogoutRedirectUris: [new Uri(blazorBaseUri, "signout-callback-oidc")]);
    }

    /// <summary>
    /// Production-style bootstrap. Creates one Enki-admin user
    /// (<c>UserType=Team</c>, <c>TeamSubtype=Office</c>) with the
    /// supplied credentials, then ensures the OpenIddict scope and
    /// Blazor client exist with the supplied secret + redirects.
    /// Idempotent: an existing email reconciles
    /// <see cref="ApplicationUser.IsEnkiAdmin"/> to true and rotates
    /// the security stamp, but never resets the password.
    /// </summary>
    public async Task BootstrapForProductionAsync(
        string adminEmail,
        string adminPassword,
        string blazorClientSecret,
        BlazorClientRedirects redirects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(blazorClientSecret);
        ArgumentNullException.ThrowIfNull(redirects);

        await EnsureAdminUserAsync(adminEmail, adminPassword);
        await EnsureOpenIddictAsync(blazorClientSecret, redirects);
    }

    /// <summary>
    /// Dev-rig bootstrap. Walks <see cref="SeedUsers.All"/> applying
    /// the create-or-reconcile pattern from the previous
    /// <c>IdentitySeedData.SeedAsync</c>: new rows get the supplied
    /// default password; existing rows reconcile admin column,
    /// classification, session lifetime, and capability claims —
    /// passwords are never overwritten. Then ensures the OpenIddict
    /// scope and Blazor client exist with the supplied secret +
    /// redirects (dev defaults).
    /// </summary>
    public async Task SeedDevRosterAsync(
        string defaultPassword,
        string blazorClientSecret,
        BlazorClientRedirects redirects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(blazorClientSecret);
        ArgumentNullException.ThrowIfNull(redirects);

        foreach (var seed in SeedUsers.All)
        {
            await EnsureSeedUserAsync(seed, defaultPassword);
        }

        await EnsureOpenIddictAsync(blazorClientSecret, redirects);
    }

    // ----------------------------------------------------------------
    // Admin user (production path)
    // ----------------------------------------------------------------

    private async Task EnsureAdminUserAsync(string email, string password)
    {
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            logger.LogInformation(
                "Admin user '{Email}' already exists; reconciling IsEnkiAdmin only " +
                "(password is never overwritten by bootstrap).",
                email);
            await ReconcileAdminColumnAsync(existing, desiredAdmin: true);
            return;
        }

        // Username = email keeps the first-deploy story simple — the
        // operator signs in with the same string they typed into
        // Identity__Seed__AdminEmail. Password complexity is enforced by
        // IdentityOptions.Password (configured on the host), so a weak
        // password supplied here surfaces as a CreateAsync failure.
        var user = new ApplicationUser
        {
            Id                 = Guid.NewGuid().ToString(),
            UserName           = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email              = email,
            NormalizedEmail    = email.ToUpperInvariant(),
            EmailConfirmed     = true,
            LockoutEnabled     = true,
            UserType           = SDI.Enki.Shared.Identity.UserType.Team,
            TeamSubtype        = TeamSubtype.Office.Name,
            IsEnkiAdmin        = true,
            SecurityStamp      = Guid.NewGuid().ToString(),
        };

        var failures = UserClassificationValidator.Validate(
            userType:    user.UserType,
            teamSubtype: TeamSubtype.Office,
            tenantId:    null,
            isEnkiAdmin: true);
        if (failures.Count > 0)
            throw new InvalidOperationException(
                "Admin user classification is invalid: " +
                string.Join("; ", failures.Select(f => $"{f.Field}: {f.Message}")));

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to create admin user '{email}': " +
                string.Join("; ", result.Errors.Select(e => e.Description)));

        logger.LogInformation(
            "Created admin user '{Email}' (Id={Id}) with EnkiAdmin=true.",
            email, user.Id);
    }

    // ----------------------------------------------------------------
    // SeedUsers roster (dev path)
    // ----------------------------------------------------------------

    private async Task EnsureSeedUserAsync(SeedUser seed, string defaultPassword)
    {
        // Validate the classification triplet up-front — same validator
        // the admin endpoints use, so a malformed seed entry fails the
        // bootstrap rather than landing an invalid AspNetUsers row that
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

        var idString = seed.IdentityId.ToString();
        var user     = await userManager.FindByIdAsync(idString);
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

            var result = await userManager.CreateAsync(user, defaultPassword);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Failed to seed user '{seed.Username}': " +
                    string.Join("; ", result.Errors.Select(e => e.Description)));

            await userManager.AddClaimsAsync(user, new[]
            {
                new System.Security.Claims.Claim("name",        $"{seed.FirstName} {seed.LastName}"),
                new System.Security.Claims.Claim("given_name",  seed.FirstName),
                new System.Security.Claims.Claim("family_name", seed.LastName),
            });
        }

        // Reconcile the admin bit for both newly-created and existing users.
        await ReconcileAdminColumnAsync(user!, seed.IsEnkiAdmin);

        // Reconcile the session-lifetime override too, so flipping the
        // value in SeedUsers takes effect on the next bootstrap without
        // a DB reset. Same stamp-rotation rationale as the admin bit:
        // any in-flight refresh token issued under the old window must
        // stop validating once the policy changes.
        await ReconcileSessionLifetimeAsync(user!, seed.SessionLifetimeMinutes);

        // Reconcile classification (TeamSubtype + TenantId). UserType
        // itself is immutable post-creation per the design — if a seed
        // entry's UserType disagrees with the existing column we throw
        // rather than silently mutating; the operator must drop the
        // user and re-seed if they truly meant to switch buckets.
        await ReconcileClassificationAsync(user!, seed);

        // Reconcile capability claims — diff existing rows against
        // seed.Capabilities and add/remove to converge. Stamp rotates
        // on any change so the new claim set takes effect on the
        // next exchange.
        await ReconcileCapabilitiesAsync(user!, seed.Capabilities);
    }

    // ----------------------------------------------------------------
    // Reconcilers — copied verbatim from previous IdentitySeedData
    // ----------------------------------------------------------------

    /// <summary>
    /// Converges the user's <see cref="ApplicationUser.IsEnkiAdmin"/>
    /// column with the desired flag. The role claim is derived from
    /// this column at sign-in by <c>EnkiUserClaimsPrincipalFactory</c>,
    /// so the seeder never touches AspNetUserClaims rows for the role.
    ///
    /// <para>
    /// On any column flip the SecurityStamp is rotated so existing
    /// refresh tokens stop validating and the next sign-in re-runs the
    /// claims factory against the new column value.
    /// </para>
    /// </summary>
    private async Task ReconcileAdminColumnAsync(ApplicationUser user, bool desiredAdmin)
    {
        if (user.IsEnkiAdmin == desiredAdmin) return;

        user.IsEnkiAdmin = desiredAdmin;
        await userManager.UpdateAsync(user);
        await userManager.UpdateSecurityStampAsync(user);
    }

    /// <summary>
    /// Converges <see cref="ApplicationUser.SessionLifetimeMinutes"/>
    /// with the desired value. Stamps the metadata columns + rotates
    /// the security stamp on any change so the new lifetime takes
    /// effect on the next exchange. No-ops when the column already
    /// matches.
    /// </summary>
    private async Task ReconcileSessionLifetimeAsync(ApplicationUser user, int? desiredMinutes)
    {
        if (user.SessionLifetimeMinutes == desiredMinutes) return;

        user.SessionLifetimeMinutes   = desiredMinutes;
        user.SessionLifetimeUpdatedAt = DateTimeOffset.UtcNow;
        user.SessionLifetimeUpdatedBy = "seed";

        await userManager.UpdateAsync(user);
        await userManager.UpdateSecurityStampAsync(user);
    }

    /// <summary>
    /// Converges classification fields (<c>TeamSubtype</c> + <c>TenantId</c>)
    /// from the seed tuple. <c>UserType</c> mismatches throw — the
    /// design forbids switching Team↔Tenant on an existing row; the
    /// operator must drop the row and reseed if they truly meant the
    /// change. Mutable changes rotate the security stamp so a
    /// downgraded user gets force-resigned at the next refresh.
    /// </summary>
    private async Task ReconcileClassificationAsync(ApplicationUser user, SeedUser seed)
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

        await userManager.UpdateAsync(user);
        await userManager.UpdateSecurityStampAsync(user);
    }

    /// <summary>
    /// Converges <c>enki:capability</c> claims with the seed entry's
    /// list. Filters to <see cref="EnkiCapabilities.IsKnown"/> entries
    /// so a typo in seed data doesn't smuggle a dead claim onto a user.
    /// Tenant users with any capability listed throw — capabilities
    /// are Team-side only and the validator already rejects the
    /// combination at every other site.
    /// </summary>
    private async Task ReconcileCapabilitiesAsync(
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

        var existing = (await userManager.GetClaimsAsync(user))
            .Where(c => c.Type == EnkiClaimTypes.Capability)
            .ToList();
        var existingValues = existing.Select(c => c.Value).ToHashSet();

        var toAdd    = desired.Except(existingValues).ToList();
        var toRemove = existing.Where(c => !desired.Contains(c.Value)).ToList();

        if (toAdd.Count == 0 && toRemove.Count == 0) return;

        foreach (var c in toRemove)
            await userManager.RemoveClaimAsync(user, c);
        foreach (var v in toAdd)
            await userManager.AddClaimAsync(user, new System.Security.Claims.Claim(EnkiClaimTypes.Capability, v));

        await userManager.UpdateSecurityStampAsync(user);
    }

    // ----------------------------------------------------------------
    // OpenIddict scope + client (shared across both bootstrap entry points)
    // ----------------------------------------------------------------

    /// <summary>
    /// Registers the WebApi scope and the Blazor Server client. The
    /// scope is straight upsert-when-absent. The client is
    /// <b>create-only</b> — once it exists, this method does NOT
    /// touch it again. That preserves any rotated secret and prevents
    /// the boot-time secret reset that an unconditional upsert path
    /// would cause.
    /// </summary>
    private async Task EnsureOpenIddictAsync(
        string blazorClientSecret,
        BlazorClientRedirects redirects)
    {
        if (await openIddictScopeManager.FindByNameAsync(AuthConstants.WebApiScope) is null)
        {
            await openIddictScopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name        = AuthConstants.WebApiScope,
                DisplayName = "Enki Web API",
                Description = "Access to Enki tenant + master data endpoints.",
                Resources   = { "resource_server_enki" },
            });
            logger.LogInformation("Created OpenIddict scope '{Scope}'.", AuthConstants.WebApiScope);
        }

        if (await openIddictAppManager.FindByClientIdAsync(BlazorClientId) is null)
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId     = BlazorClientId,
                ClientSecret = blazorClientSecret,
                DisplayName  = BlazorClientName,
                ConsentType  = ConsentTypes.Implicit,
                ClientType   = ClientTypes.Confidential,
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
            };
            foreach (var u in redirects.RedirectUris)
                descriptor.RedirectUris.Add(u);
            foreach (var u in redirects.PostLogoutRedirectUris)
                descriptor.PostLogoutRedirectUris.Add(u);

            await openIddictAppManager.CreateAsync(descriptor);
            logger.LogInformation(
                "Created OpenIddict client '{ClientId}' with {RedirectCount} redirect URI(s).",
                BlazorClientId, redirects.RedirectUris.Count);
        }
        else
        {
            logger.LogInformation(
                "OpenIddict client '{ClientId}' already present; secret + redirects left untouched.",
                BlazorClientId);
        }
    }
}
