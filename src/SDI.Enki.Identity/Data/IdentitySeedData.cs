using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
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

    // Id GUIDs must match SDI.Enki.Core.Master.Users.MasterSeedData so the
    // master-DB User.IdentityId == AspNetUsers.Id end-to-end.
    // IsAdmin gates emission of the enki-admin role claim — flip it here
    // until the admin-management UI exists.
    private static readonly (string Id, string Username, string Email, string FirstName, string LastName, bool IsAdmin)[] Users =
    {
        ("8cf4b730-c619-49d0-8ed7-be0ac89de718", "dapo.ajayi",      "dapo.ajayi@scientificdrilling.com",      "Dapo",    "Ajayi",    false),
        ("f8aff5b3-473b-436f-9592-186cb28ac848", "jamie.dorey",     "jamie.dorey@scientificdrilling.com",     "Jamie",   "Dorey",    false),
        ("dafd065f-4790-4235-9db0-6f47abadf3aa", "adam.karabasz",   "adam.karabasz@scientificdrilling.com",   "Adam",    "Karabasz", false),
        ("bd34385d-2d88-4781-bef5-e955ddaa8293", "douglas.ridgway", "douglas.ridgway@scientificdrilling.com", "Douglas", "Ridgway",  false),
        ("e5a7f984-688a-4904-8155-3fe724584385", "travis.solomon",  "travis.solomon@scientificdrilling.com",  "Travis",  "Solomon",  false),
        ("1e333b45-1448-4b26-a68d-b4effbbdcd9d", "mike.king",       "mike.king@scientificdrilling.com",       "Mike",    "King",     true ),
        ("a72f07d8-9a12-4825-95f4-7c5bbea6e6e5", "james.powell",    "james.powell@scientificdrilling.com",    "James",   "Powell",   false),
        ("f8d3ceda-ce98-4825-88f9-c8e8356a61db", "joel.harrison",   "joel.harrison@scientificdrilling.com",   "Joel",    "Harrison", false),
        ("bc120086-fc2d-4f41-b76a-3f6c3536c2cc", "scott.brandel",   "scott.brandel@scientificdrilling.com",   "Scott",   "Brandel",  false),
        ("d92be0d5-dfbe-4d1d-9823-1ca37617dade", "john.borders",    "john.borders@scientificdrilling.com",    "John",    "Borders",  false),
        ("92473a14-0196-42ed-b098-9c3d85505f8d", "karl.king",       "karl.king@scientificdrilling.com",       "Karl",    "King",     false),
        ("2c4f110e-adc4-4759-aa34-b73ec0954c9e", "gavin.helboe",    "gavin.helboe@scientificdrilling.com",    "Gavin",   "Helboe",   false),
    };

    /// <summary>
    /// Apply at host startup. Uses the default development password
    /// <c>Enki!dev1</c> for all seeded users — rotate via the admin UI in
    /// any non-dev environment. This matches the "commit creds to dev repo"
    /// stance already accepted for <c>appsettings.Development.json</c>.
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var (id, username, email, firstName, lastName, isAdmin) in Users)
        {
            // Two-phase idempotency:
            //   1. If the user doesn't exist, create + add the baseline claims
            //      (name / given_name / family_name) in one go.
            //   2. In either branch, reconcile the IsEnkiAdmin flag so flipping
            //      the seed tuple takes effect on the next boot without needing
            //      to drop the user. The existing user's SecurityStamp is
            //      rotated on role change so any live refresh tokens are
            //      invalidated and the next sign-in issues a fresh role claim.
            var user = await userMgr.FindByIdAsync(id);
            var creating = user is null;

            if (creating)
            {
                user = new ApplicationUser
                {
                    Id               = id,
                    UserType         = "Team",
                    UserName         = username,
                    NormalizedUserName = username.ToUpperInvariant(),
                    Email            = email,
                    NormalizedEmail  = email.ToUpperInvariant(),
                    EmailConfirmed   = true,
                    LockoutEnabled   = true,
                    IsEnkiAdmin      = isAdmin,
                    SecurityStamp    = Guid.NewGuid().ToString(),
                };

                var result = await userMgr.CreateAsync(user, "Enki!dev1");
                if (!result.Succeeded)
                    throw new InvalidOperationException(
                        $"Failed to seed user '{username}': " + string.Join("; ", result.Errors.Select(e => e.Description)));

                await userMgr.AddClaimsAsync(user, new[]
                {
                    new System.Security.Claims.Claim("name",        $"{firstName} {lastName}"),
                    new System.Security.Claims.Claim("given_name",  firstName),
                    new System.Security.Claims.Claim("family_name", lastName),
                });
            }

            // Reconcile the admin bit for both newly-created and existing users.
            await ReconcileAdminRoleAsync(userMgr, user!, isAdmin);
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
