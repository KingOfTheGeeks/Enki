using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using SDI.Enki.Identity.Bootstrap;
using SDI.Enki.Identity.Data;
using SDI.Enki.Shared.Identity;
using SDI.Enki.Shared.Seeding;

namespace SDI.Enki.Migrator.Tests;

/// <summary>
/// Coverage for <see cref="IdentityBootstrapper"/>'s production and
/// dev-roster paths against a real Identity DB hosted on a
/// Testcontainers-managed SQL Server 2022 instance. EF InMemory can't
/// host the OpenIddict EF stores, so a real DB is the only honest way
/// to exercise the create-only client + idempotent admin reconciler.
///
/// <para>
/// Excluded from the default test run via
/// <c>[Trait("Category", "Sql")]</c> so plain <c>dotnet test</c> stays
/// fast; opt in with <c>--filter Category=Sql</c> when explicitly
/// testing the bootstrap path.
/// </para>
/// </summary>
[Collection("Migrator Sql")]
[Trait("Category", "Sql")]
public class IdentityBootstrapperTests
{
    private readonly IdentityDbFixture _fx;

    public IdentityBootstrapperTests(IdentityDbFixture fx) => _fx = fx;

    private async Task<(ServiceProvider Sp, string ConnString)> ProvisionEmptyDbAsync()
    {
        var connString = _fx.CreateFreshIdentityDatabase();
        var sp         = _fx.BuildIdentityServices(connString);

        // Migrate to current head so the OpenIddict + AspNet tables exist
        // — the bootstrapper itself doesn't migrate, that's the
        // BootstrapEnvironmentCommand's job (via Database.MigrateAsync).
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        return (sp, connString);
    }

    [SkippableFact]
    public async Task BootstrapForProductionAsync_OnEmptyDb_CreatesAdminAndOpenIddictClient()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var (sp, _) = await ProvisionEmptyDbAsync();

        await using (var scope = sp.CreateAsyncScope())
        {
            var bootstrapper = scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>();
            await bootstrapper.BootstrapForProductionAsync(
                adminEmail:         "admin@enki.test",
                adminPassword:      "Bootstrap!1A",
                blazorClientSecret: "test-secret",
                redirects:          IdentityBootstrapper.FromBlazorBaseUri(
                                        new Uri("https://test.local/")));
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = await db.Users.AsNoTracking().ToListAsync();
            var admin = Assert.Single(users);
            Assert.Equal("admin@enki.test", admin.Email);
            Assert.True(admin.IsEnkiAdmin);
            Assert.Equal(UserType.Team, admin.UserType);
            Assert.Equal(TeamSubtype.Office.Name, admin.TeamSubtype);

            // Bootstrap-created admin can authenticate with the supplied password.
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var fetched = await userMgr.FindByEmailAsync("admin@enki.test");
            Assert.NotNull(fetched);
            Assert.True(await userMgr.CheckPasswordAsync(fetched!, "Bootstrap!1A"));

            // OpenIddict scope + client landed.
            var scopeMgr = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
            Assert.NotNull(await scopeMgr.FindByNameAsync(AuthConstants.WebApiScope));

            var appMgr = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var app    = await appMgr.FindByClientIdAsync(IdentityBootstrapper.BlazorClientId);
            Assert.NotNull(app);
            // Client is confidential — secret is hashed-stored. We can't
            // round-trip "is this our exact secret"; the runtime check is
            // that the secret was set at all (CreateAsync fails on null).
            Assert.True(await appMgr.HasClientTypeAsync(app!,
                OpenIddictConstants.ClientTypes.Confidential));
        }

        await sp.DisposeAsync();
    }

    [SkippableFact]
    public async Task BootstrapForProductionAsync_RunTwice_IsIdempotent_PasswordPreserved()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var (sp, _) = await ProvisionEmptyDbAsync();

        async Task RunBootstrap(string password)
        {
            await using var scope = sp.CreateAsyncScope();
            var bootstrapper = scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>();
            await bootstrapper.BootstrapForProductionAsync(
                adminEmail:         "admin@enki.test",
                adminPassword:      password,
                blazorClientSecret: "test-secret",
                redirects:          IdentityBootstrapper.FromBlazorBaseUri(
                                        new Uri("https://test.local/")));
        }

        // First run creates the user with PasswordA.
        await RunBootstrap("PasswordA!1A");

        // Second run with a DIFFERENT password — must NOT reset the
        // existing user's password (that's the idempotency contract:
        // a re-bootstrap after manual rotation is safe).
        await RunBootstrap("PasswordB!1B");

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Single(await db.Users.AsNoTracking().ToListAsync());

            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin   = await userMgr.FindByEmailAsync("admin@enki.test");
            Assert.NotNull(admin);

            // Original password still works…
            Assert.True(await userMgr.CheckPasswordAsync(admin!, "PasswordA!1A"));
            // …the second-call password did not overwrite.
            Assert.False(await userMgr.CheckPasswordAsync(admin!, "PasswordB!1B"));

            // OpenIddict client unchanged (only one row).
            var appMgr = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var apps   = new List<object>();
            await foreach (var a in appMgr.ListAsync())
                apps.Add(a);
            Assert.Single(apps);
        }

        await sp.DisposeAsync();
    }

    [SkippableFact]
    public async Task SeedDevRosterAsync_OnEmptyDb_CreatesAllSeedUsers_AndClient()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        var (sp, _) = await ProvisionEmptyDbAsync();

        await using (var scope = sp.CreateAsyncScope())
        {
            var bootstrapper = scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>();
            await bootstrapper.SeedDevRosterAsync(
                defaultPassword:    "Enki!dev1",
                blazorClientSecret: "test-secret",
                redirects:          IdentityBootstrapper.DevRedirects());
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var db    = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = await db.Users.AsNoTracking().ToListAsync();

            // Every SeedUser landed.
            Assert.Equal(SeedUsers.All.Count, users.Count);
            foreach (var seed in SeedUsers.All)
            {
                Assert.Contains(users, u => u.Id == seed.IdentityId.ToString());
            }

            // Mike + Gavin are the seeded admins.
            Assert.Equal(2, users.Count(u => u.IsEnkiAdmin));
            Assert.Contains(users, u => u.IsEnkiAdmin && u.UserName == "mike.king");
            Assert.Contains(users, u => u.IsEnkiAdmin && u.UserName == "gavin.helboe");

            // Mike's session-lifetime override was persisted.
            var mike = users.Single(u => u.UserName == "mike.king");
            Assert.Equal(525600, mike.SessionLifetimeMinutes);
        }

        await sp.DisposeAsync();
    }
}
