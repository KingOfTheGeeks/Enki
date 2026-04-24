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

    // Id GUIDs must match SDI.Enki.Core.Master.Users.MasterSeedData so the
    // master-DB User.IdentityId == AspNetUsers.Id end-to-end.
    private static readonly (string Id, string Username, string Email, string FirstName, string LastName)[] Users =
    {
        ("8cf4b730-c619-49d0-8ed7-be0ac89de718", "dapo.ajayi",      "dapo.ajayi@scientificdrilling.com",      "Dapo",    "Ajayi"),
        ("f8aff5b3-473b-436f-9592-186cb28ac848", "jamie.dorey",     "jamie.dorey@scientificdrilling.com",     "Jamie",   "Dorey"),
        ("dafd065f-4790-4235-9db0-6f47abadf3aa", "adam.karabasz",   "adam.karabasz@scientificdrilling.com",   "Adam",    "Karabasz"),
        ("bd34385d-2d88-4781-bef5-e955ddaa8293", "douglas.ridgway", "douglas.ridgway@scientificdrilling.com", "Douglas", "Ridgway"),
        ("e5a7f984-688a-4904-8155-3fe724584385", "travis.solomon",  "travis.solomon@scientificdrilling.com",  "Travis",  "Solomon"),
        ("1e333b45-1448-4b26-a68d-b4effbbdcd9d", "mike.king",       "mike.king@scientificdrilling.com",       "Mike",    "King"),
        ("a72f07d8-9a12-4825-95f4-7c5bbea6e6e5", "james.powell",    "james.powell@scientificdrilling.com",    "James",   "Powell"),
        ("f8d3ceda-ce98-4825-88f9-c8e8356a61db", "joel.harrison",   "joel.harrison@scientificdrilling.com",   "Joel",    "Harrison"),
        ("bc120086-fc2d-4f41-b76a-3f6c3536c2cc", "scott.brandel",   "scott.brandel@scientificdrilling.com",   "Scott",   "Brandel"),
        ("d92be0d5-dfbe-4d1d-9823-1ca37617dade", "john.borders",    "john.borders@scientificdrilling.com",    "John",    "Borders"),
        ("92473a14-0196-42ed-b098-9c3d85505f8d", "karl.king",       "karl.king@scientificdrilling.com",       "Karl",    "King"),
        ("2c4f110e-adc4-4759-aa34-b73ec0954c9e", "gavin.helboe",    "gavin.helboe@scientificdrilling.com",    "Gavin",   "Helboe"),
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

        foreach (var (id, username, email, firstName, lastName) in Users)
        {
            if (await userMgr.FindByIdAsync(id) is not null) continue;

            var user = new ApplicationUser
            {
                Id               = id,
                UserType         = "Team",
                UserName         = username,
                NormalizedUserName = username.ToUpperInvariant(),
                Email            = email,
                NormalizedEmail  = email.ToUpperInvariant(),
                EmailConfirmed   = true,
                LockoutEnabled   = true,
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

        await SeedOpenIddictAsync(scope.ServiceProvider);
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

        // Blazor Server client — authorization-code + refresh tokens. Redirect
        // URIs are the dev defaults; prod values override via appsettings.
        if (await clientMgr.FindByClientIdAsync(BlazorClientId) is null)
        {
            await clientMgr.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId     = BlazorClientId,
                ClientSecret = "enki-blazor-dev-secret",       // dev only; override per env
                DisplayName  = BlazorClientName,
                ConsentType  = ConsentTypes.Implicit,
                ClientType   = ClientTypes.Confidential,
                RedirectUris      = { new Uri("https://localhost:4001/signin-oidc") },
                PostLogoutRedirectUris = { new Uri("https://localhost:4001/signout-callback-oidc") },
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
    }
}
