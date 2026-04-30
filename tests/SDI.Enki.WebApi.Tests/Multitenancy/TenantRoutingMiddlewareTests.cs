using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning;
using SDI.Enki.Infrastructure.Provisioning.Models;
using SDI.Enki.WebApi.Multitenancy;

namespace SDI.Enki.WebApi.Tests.Multitenancy;

/// <summary>
/// Direct middleware tests. The integration tests in
/// <c>Integration/TenantsEndpointSmokeTests</c> et al. exercise the same
/// code path through the full pipeline; these unit-level tests pin the
/// metadata-bypass branch in isolation so a regression there fails fast
/// without booting a TestServer.
/// </summary>
public class TenantRoutingMiddlewareTests
{
    // ---------- fixture helpers ----------

    private static EnkiMasterDbContext NewMaster(
        [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseInMemoryDatabase($"middleware-{name}-{Guid.NewGuid():N}")
            .Options;
        return new EnkiMasterDbContext(opts);
    }

    /// <summary>
    /// Builds a request with <c>tenantCode</c> already on RouteValues +
    /// an Endpoint with the supplied metadata. The middleware is what
    /// would normally read these — both are populated by ASP.NET Core's
    /// routing middleware in production, which runs before us.
    /// </summary>
    private static DefaultHttpContext BuildContext(string tenantCode, params object[] endpointMetadata)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.RouteValues["tenantCode"] = tenantCode;
        ctx.Response.Body = new MemoryStream();

        var endpoint = new Endpoint(
            requestDelegate: _ => Task.CompletedTask,
            metadata: new EndpointMetadataCollection(endpointMetadata),
            displayName: "test");
        ctx.SetEndpoint(endpoint);
        return ctx;
    }

    private static ProblemDetailsFactory NewProblemFactory()
    {
        var options = Options.Create(new Microsoft.AspNetCore.Mvc.ApiBehaviorOptions());
        return new Microsoft.AspNetCore.Mvc.Infrastructure.DefaultProblemDetailsFactory(options);
    }

    // ============================================================
    // Bypass — [SkipTenantRouting]
    // ============================================================

    [Fact]
    public async Task SkipTenantRouting_OnEndpoint_PassesThroughEvenForInactiveTenant()
    {
        // The whole point of issue #23: master-registry endpoints
        // (TenantsController) carry [SkipTenantRouting] and must reach
        // their action even when the tenant is Inactive. Without the
        // bypass the middleware queries master, sees Status != Active,
        // 404s — and Reactivate becomes unreachable.
        await using var db = NewMaster();
        db.Tenants.Add(new Tenant("ACME", "Acme Corp")
        {
            Status = TenantStatus.Inactive,
            DeactivatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var ctx = BuildContext("ACME", new SkipTenantRoutingAttribute());
        var nextCalled = false;

        var middleware = new TenantRoutingMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            ctx, db,
            databaseAdmin: null!,   // not consulted on the bypass branch
            cache: new MemoryCache(new MemoryCacheOptions()),
            problemDetailsFactory: NewProblemFactory(),
            logger: NullLogger<TenantRoutingMiddleware>.Instance);

        Assert.True(nextCalled,
            "[SkipTenantRouting] must short-circuit to next() — that's how master-registry endpoints reach their action for non-Active tenants.");
        Assert.NotEqual(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task NoSkipTenantRouting_InactiveTenant_Still404s()
    {
        // Negative regression: without the attribute the middleware's
        // hard-revocation rule is unchanged — Inactive tenants still
        // 404 on tenant-scoped routes. We must not have accidentally
        // opened a hole that lets traffic through to /jobs, /runs etc.
        // for a deactivated tenant.
        await using var db = NewMaster();
        var tenant = new Tenant("ACME", "Acme Corp")
        {
            Status = TenantStatus.Inactive,
            DeactivatedAt = DateTimeOffset.UtcNow,
        };
        db.Tenants.Add(tenant);
        db.TenantDatabases.Add(new TenantDatabase(
            tenant.Id, TenantDatabaseKind.Active,  "test-server", "Enki_ACME_Active"));
        db.TenantDatabases.Add(new TenantDatabase(
            tenant.Id, TenantDatabaseKind.Archive, "test-server", "Enki_ACME_Archive"));
        await db.SaveChangesAsync();

        // Endpoint metadata empty — represents a tenant-scoped controller.
        var ctx = BuildContext("ACME");
        var nextCalled = false;

        var middleware = new TenantRoutingMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            ctx, db,
            databaseAdmin: null!,
            cache: new MemoryCache(new MemoryCacheOptions()),
            problemDetailsFactory: NewProblemFactory(),
            logger: NullLogger<TenantRoutingMiddleware>.Instance);

        Assert.False(nextCalled,
            "Inactive tenants must still be revoked on tenant-scoped routes (no [SkipTenantRouting]).");
        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task NoSkipTenantRouting_ActiveTenant_PopulatesContextAndCallsNext()
    {
        // Sanity check on the happy path: tenant-scoped routes against
        // an Active tenant flow through to next() with TenantContext
        // populated. Mostly here so a regression that shorts the bypass
        // branch (e.g. always passing through) fails this test loudly
        // alongside the positive #23 case.
        await using var db = NewMaster();
        var tenant = new Tenant("ACME", "Acme Corp") { Status = TenantStatus.Active };
        db.Tenants.Add(tenant);
        db.TenantDatabases.Add(new TenantDatabase(
            tenant.Id, TenantDatabaseKind.Active,  "test-server", "Enki_ACME_Active"));
        db.TenantDatabases.Add(new TenantDatabase(
            tenant.Id, TenantDatabaseKind.Archive, "test-server", "Enki_ACME_Archive"));
        await db.SaveChangesAsync();

        var ctx = BuildContext("ACME");
        var nextCalled = false;

        var provisioningOptions = new ProvisioningOptions(
            MasterConnectionString: "Server=(test);Database=enki-master;Integrated Security=true;");
        var dbAdmin = new DatabaseAdmin(provisioningOptions, NullLogger<DatabaseAdmin>.Instance);

        var middleware = new TenantRoutingMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            ctx, db, dbAdmin,
            cache: new MemoryCache(new MemoryCacheOptions()),
            problemDetailsFactory: NewProblemFactory(),
            logger: NullLogger<TenantRoutingMiddleware>.Instance);

        Assert.True(nextCalled);
        var resolved = Assert.IsType<TenantContext>(ctx.Items[TenantContext.ItemKey]);
        Assert.Equal("ACME", resolved.Code);
        Assert.Equal(tenant.Id, resolved.TenantId);
    }
}
