using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.Isolation.Tests;

/// <summary>
/// Test factory that hosts WebApi against InMemory for both the master
/// DB and per-tenant DBs. Authentication is replaced with a
/// <see cref="IsolationAuthHandler"/> that grants the <c>enki-admin</c>
/// role so every request bypasses the per-tenant membership check —
/// the isolation tests are about <i>data</i> isolation, not auth.
/// </summary>
public sealed class IsolationTestFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Per-fixture salt baked into every InMemory store name (master +
    /// per-tenant). Keeps parallel test classes from sharing state.
    /// </summary>
    public string FixtureId { get; } = Guid.NewGuid().ToString("N");

    public string MasterDbName => $"isolation-master-{FixtureId}";

    public IsolationTestFactory()
    {
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
        var name = $"isolation-{FixtureId}-{code}-{kind}";
        var opts = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new TenantDbContext(opts);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        var fixtureId = FixtureId;

        builder.ConfigureServices(services =>
        {
            // Replace master DbContext with InMemory.
            var dropMaster = services
                .Where(d => d.ServiceType.FullName?.Contains(nameof(AthenaMasterDbContext)) == true
                         || d.ServiceType == typeof(DbContextOptions<AthenaMasterDbContext>))
                .ToList();
            foreach (var d in dropMaster) services.Remove(d);
            services.AddDbContext<AthenaMasterDbContext>(opt =>
                opt.UseInMemoryDatabase(MasterDbName));

            // Replace ITenantDbContextFactory with the isolating one.
            // Capturing fixtureId in the closure pins the store-name suffix
            // across DI scopes — every scope that asks for the factory
            // hits the same per-tenant InMemory store.
            services.RemoveAll<ITenantDbContextFactory>();
            services.AddScoped<ITenantDbContextFactory>(sp =>
                new IsolatingTenantDbContextFactory(
                    sp.GetRequiredService<IHttpContextAccessor>(),
                    fixtureId));

            // Replace auth with a Test scheme that grants enki-admin.
            services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();
            services.AddAuthentication(IsolationAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, IsolationAuthHandler>(
                    IsolationAuthHandler.SchemeName, _ => { });
        });
    }
}

internal sealed class IsolationAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    public IsolationAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim("sub",    "11111111-1111-1111-1111-111111111111"),
            new Claim("name",   "isolation.test"),
            new Claim("oi_scp", "enki"),
            new Claim("role",   "enki-admin"),
        };
        var identity  = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName)));
    }
}
