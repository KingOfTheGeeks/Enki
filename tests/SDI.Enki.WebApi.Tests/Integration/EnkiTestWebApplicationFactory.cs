using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Tests.Integration;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> that swaps the
/// WebApi's three heavy environmental dependencies for test equivalents:
///
/// <list type="bullet">
///   <item><see cref="AthenaMasterDbContext"/> → EF InMemory keyed on a
///   per-fixture id so parallel test classes don't cross-pollute.</item>
///   <item><see cref="ITenantDbContextFactory"/> → an isolating fake
///   that maps the route's <c>{tenantCode}</c> to a per-tenant InMemory
///   store. Tests can seed any tenant's store via
///   <see cref="OpenTenantStore"/>.</item>
///   <item>Authentication scheme → <see cref="TestAuthHandler"/>. Per
///   request, headers (<c>X-Test-Sub</c>, <c>X-Test-Admin</c>,
///   <c>X-Test-Anonymous</c>) override the per-fixture defaults.</item>
/// </list>
/// </summary>
public sealed class EnkiTestWebApplicationFactory : WebApplicationFactory<Program>
{
    public string FixtureId   { get; } = Guid.NewGuid().ToString("N");
    public string MasterDbName => $"enki-master-{FixtureId}";

    public EnkiTestWebApplicationFactory()
    {
        // WebApplicationBuilder reads config at the top of Program.cs —
        // before ConfigureWebHost / ConfigureServices run. Env vars are
        // applied to the builder's default configuration sources.
        Environment.SetEnvironmentVariable("ConnectionStrings__Master",
            "Server=(test);Database=test;Integrated Security=true;");
        Environment.SetEnvironmentVariable("Identity__Issuer", "http://localhost.test/");
    }

    /// <summary>
    /// Test-side handle on the same per-tenant InMemory store the
    /// runtime factory will resolve to for the given tenant code.
    /// Use to seed before firing requests.
    /// </summary>
    public TenantDbContext OpenTenantStore(string code, string kind = "active")
    {
        var name = $"enki-tenant-{FixtureId}-{code}-{kind}";
        var opts = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new TenantDbContext(opts);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");   // skip the dev auto-migrate + DevMasterSeeder

        var fixtureId = FixtureId;

        builder.ConfigureServices(services =>
        {
            // Master DbContext → InMemory.
            var dropTypes = services
                .Where(d => d.ServiceType.FullName?.Contains(nameof(AthenaMasterDbContext)) == true
                         || d.ServiceType == typeof(DbContextOptions<AthenaMasterDbContext>))
                .ToList();
            foreach (var d in dropTypes) services.Remove(d);
            services.AddDbContext<AthenaMasterDbContext>(opt =>
                opt.UseInMemoryDatabase(MasterDbName));

            // Tenant context factory → routes the request's TenantContext
            // to the matching InMemory store. Same store-name scheme as
            // OpenTenantStore so tests + runtime see identical data.
            services.RemoveAll<ITenantDbContextFactory>();
            services.AddScoped<ITenantDbContextFactory>(sp =>
                new InMemoryTenantDbContextFactory(
                    sp.GetRequiredService<IHttpContextAccessor>(),
                    fixtureId));

            // Auth → TestAuthHandler.
            services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<TestAuthHandlerOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });
        });
    }

    /// <summary>
    /// Master-DB seed helper. Disposes the scope + context — callers
    /// shouldn't hold the returned context across requests.
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

/// <summary>
/// Implementation of <see cref="ITenantDbContextFactory"/> for tests:
/// each call resolves the request's <c>TenantContext.Code</c> and hands
/// out a fresh InMemory <see cref="TenantDbContext"/> keyed on that
/// code + the fixture id. Same scheme as
/// <see cref="EnkiTestWebApplicationFactory.OpenTenantStore"/> so the
/// arrange + act phases of a test see the same store.
/// </summary>
internal sealed class InMemoryTenantDbContextFactory(
    IHttpContextAccessor http,
    string fixtureId) : ITenantDbContextFactory
{
    public TenantDbContext CreateActive()  => Build("active");
    public TenantDbContext CreateArchive() => Build("archive");

    private TenantDbContext Build(string kind)
    {
        var ctx = http.HttpContext
            ?? throw new InvalidOperationException("No HttpContext on this thread.");
        if (ctx.Items[TenantContext.ItemKey] is not TenantContext tenant)
            throw new InvalidOperationException(
                "No TenantContext on the request — TenantRoutingMiddleware should have populated it.");

        var name = $"enki-tenant-{fixtureId}-{tenant.Code}-{kind}";
        var opts = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new TenantDbContext(opts);
    }
}
