using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using SDI.Enki.Shared.Seeding;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SDI.Enki.Identity.Data;

/// <summary>
/// Seeds the Enki Identity server with its canonical users, the WebApi
/// resource scope, and the Blazor client registration. Idempotent — runs at
/// startup and skips rows that already exist. This is the OpenIddict +
/// ASP.NET Identity equivalent of legacy Athena's Identity seed.
/// </summary>
public static class IdentitySeedData
{
    public const string WebApiScope      = "enki";
    public const string BlazorClientId   = "enki-blazor";
    public const string BlazorClientName = "Enki Blazor Server";

    /// <summary>
    /// Role claim value for SDI-side cross-tenant admins. Must match the
    /// constant on <c>CanAccessTenantHandler.AdminRole</c> — if those drift,
    /// the policy silently stops matching and every tenant page 403s. Kept
    /// as a string literal rather than shared in a Common project because
    /// Identity intentionally doesn't reference WebApi or its Authorization
    /// layer.
    /// </summary>
    public const string EnkiAdminRole = "enki-admin";

    /// <summary>
    /// Apply at host startup. Uses the default development password
    /// <c>Enki!dev1</c> for all seeded users — rotate via the admin UI in
    /// any non-dev environment. This matches the "commit creds to dev repo"
    /// stance already accepted for <c>appsettings.Development.json</c>.
    ///
    /// User identities come from <see cref="SeedUsers.All"/> — the same
    /// canonical roster the master-DB seed reads. Centralising both
    /// AspNetUsers.Id (<see cref="SeedUser.IdentityId"/>) and the master
    /// User.Id (<see cref="SeedUser.MasterUserId"/>) in one place
    /// guarantees they can't drift across the two contexts.
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var seed in SeedUsers.All)
        {
            // Two-phase idempotency:
            //   1. If the user doesn't exist, create + add the baseline claims
            //      (name / given_name / family_name) in one go.
            //   2. In either branch, reconcile the IsEnkiAdmin flag so flipping
            //      the seed tuple takes effect on the next boot without needing
            //      to drop the user. The existing user's SecurityStamp is
            //      rotated on role change so any live refresh tokens are
            //      invalidated and the next sign-in issues a fresh role claim.
            var idString = seed.IdentityId.ToString();
            var user = await userMgr.FindByIdAsync(idString);
            var creating = user is null;

            if (creating)
            {
                user = new ApplicationUser
                {
                    Id               = idString,
                    UserType         = "Team",
                    UserName         = seed.Username,
                    NormalizedUserName = seed.Username.ToUpperInvariant(),
                    Email            = seed.Email,
                    NormalizedEmail  = seed.Email.ToUpperInvariant(),
                    EmailConfirmed   = true,
                    LockoutEnabled   = true,
                    IsEnkiAdmin      = seed.IsEnkiAdmin,
                    SecurityStamp    = Guid.NewGuid().ToString(),
                };

                var result = await userMgr.CreateAsync(user, "Enki!dev1");
                if (!result.Succeeded)
                    throw new InvalidOperationException(
                        $"Failed to seed user '{seed.Username}': " + string.Join("; ", result.Errors.Select(e => e.Description)));

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

        await SeedOpenIddictAsync(scope.ServiceProvider);
    }

    /// <summary>
    /// Converges the user's stored role claim with the <c>isAdmin</c> flag
    /// from the seed tuple + the <see cref="ApplicationUser.IsEnkiAdmin"/>
    /// column. Adds the <c>role=enki-admin</c> claim if missing, removes
    /// it if present on a non-admin, and rotates the SecurityStamp on any
    /// change so outstanding refresh tokens no longer mint stale claims.
    ///
    /// Uses a direct user claim (not AspNet Identity roles) to avoid
    /// seeding an IdentityRole row for a single cross-tenant flag.
    /// <c>SignInManager.CreateUserPrincipalAsync</c> includes user claims
    /// in the principal, which is how it reaches the access token.
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

        // Force existing tokens to stop working — next sign-in picks up the
        // new claim set. Cheap insurance against a stale refresh token
        // out-living a role demotion.
        await userMgr.UpdateSecurityStampAsync(user);
    }

    /// <summary>
    /// Registers the WebApi scope and the Blazor Server client. Idempotent.
    /// </summary>
    private static async Task SeedOpenIddictAsync(IServiceProvider sp)
    {
        var scopeMgr  = sp.GetRequiredService<IOpenIddictScopeManager>();
        var clientMgr = sp.GetRequiredService<IOpenIddictApplicationManager>();

        // Scope for the WebApi resource.
        if (await scopeMgr.FindByNameAsync(WebApiScope) is null)
        {
            await scopeMgr.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name         = WebApiScope,
                DisplayName  = "Enki Web API",
                Description  = "Access to Enki tenant + master data endpoints.",
                Resources    = { "resource_server_enki" },
            });
        }

        // Blazor Server client — authorization-code + refresh tokens.
        // Upserts so restarts pick up config changes (redirect URIs, new
        // grants, etc.) without needing to drop the OpenIddict tables.
        var blazorDescriptor = new OpenIddictApplicationDescriptor
        {
            ClientId     = BlazorClientId,
            ClientSecret = "enki-blazor-dev-secret",   // dev only; override per env
            DisplayName  = BlazorClientName,
            ConsentType  = ConsentTypes.Implicit,
            ClientType   = ClientTypes.Confidential,
            RedirectUris =
            {
                // Dev: Blazor Server launchSettings uses these two ports.
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
        };

        var existing = await clientMgr.FindByClientIdAsync(BlazorClientId);
        if (existing is null)
            await clientMgr.CreateAsync(blazorDescriptor);
        else
            await clientMgr.UpdateAsync(existing, blazorDescriptor);
    }
}
