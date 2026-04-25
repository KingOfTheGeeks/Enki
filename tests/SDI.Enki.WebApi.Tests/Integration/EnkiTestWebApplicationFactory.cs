using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.WebApi.Tests.Integration;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> that swaps the
/// real WebApi's two heavy environmental dependencies for test
/// equivalents:
///
/// <list type="bullet">
///   <item><see cref="AthenaMasterDbContext"/> registers against EF
///   InMemory rather than SQL Server. A unique store name per fixture
///   keeps parallel tests from cross-pollinating.</item>
///   <item>Authentication redirects to <see cref="TestAuthHandler"/>
///   so policies (<c>EnkiApiScope</c>, <c>CanAccessTenant</c>) see a
///   synthesised principal with the right scope + role claims.</item>
/// </list>
///
/// Tests that need to seed the master DB before the request fires can
/// resolve the context via <see cref="SeedMaster"/>.
/// </summary>
public sealed class EnkiTestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"enki-master-{Guid.NewGuid():N}";

    public EnkiTestWebApplicationFactory()
    {
        // WebApplicationBuilder reads config at the top of Program.cs —
        // before ConfigureWebHost / ConfigureServices run. Env vars are
        // applied to the builder's default configuration sources, so
        // setting them here (in the factory constructor, before the
        // host is built) puts the values where Program.cs looks.
        // The connection string is a placeholder; ConfigureServices below
        // swaps the master DbContext to InMemory so the value is never
        // actually used to connect.
        Environment.SetEnvironmentVariable("ConnectionStrings__Master",
            "Server=(test);Database=test;Integrated Security=true;");
        Environment.SetEnvironmentVariable("Identity__Issuer", "http://localhost.test/");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");   // skip the dev auto-migrate + DevMasterSeeder

        builder.ConfigureServices(services =>
        {
            // Strip every existing master DbContext registration (option +
            // pooled factory + the context itself) so EF doesn't complain
            // about a SqlServer + InMemory provider conflict.
            var dropTypes = services
                .Where(d => d.ServiceType.FullName?.Contains(nameof(AthenaMasterDbContext)) == true
                         || d.ServiceType == typeof(DbContextOptions<AthenaMasterDbContext>))
                .ToList();
            foreach (var d in dropTypes) services.Remove(d);

            services.AddDbContext<AthenaMasterDbContext>(opt =>
                opt.UseInMemoryDatabase(_dbName));

            // Replace OpenIddict.Validation with a Test scheme that
            // synthesises whatever principal the test wants.
            services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<TestAuthHandlerOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });
        });
    }

    /// <summary>
    /// Hand-back to the test for arrange-style master DB seeding.
    /// Disposes the scope + context — callers shouldn't hold the
    /// returned context across requests.
    /// </summary>
    public async Task SeedMasterAsync(Func<AthenaMasterDbContext, Task> seed)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AthenaMasterDbContext>();
        await db.Database.EnsureCreatedAsync();
        await seed(db);
        await db.SaveChangesAsync();
    }
}
