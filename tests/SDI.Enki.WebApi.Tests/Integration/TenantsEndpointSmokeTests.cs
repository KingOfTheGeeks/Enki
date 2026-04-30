using System.Net;
using System.Net.Http.Json;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Shared.Tenants;
using SDI.Enki.WebApi.Concurrency;

namespace SDI.Enki.WebApi.Tests.Integration;

/// <summary>
/// End-to-end smoke test through the full WebApi pipeline: middleware,
/// routing, authentication, authorization policy, controller, EF query,
/// JSON serialisation. The point is not to retest the controller
/// surface (TenantsControllerTests already does that against the
/// controller directly) — it's to pin the wiring so a future "I added
/// a new controller and forgot the policy" or "I broke the auth
/// scheme registration" regression fails CI loudly.
///
/// <para>
/// The TestAuthHandler grants the principal the <c>enki</c> scope and
/// the <c>enki-admin</c> role, which together satisfy both
/// <c>EnkiApiScope</c> (the master-registry endpoints) and
/// <c>CanAccessTenant</c> (the tenant-scoped ones).
/// </para>
/// </summary>
public class TenantsEndpointSmokeTests : IClassFixture<EnkiTestWebApplicationFactory>
{
    private readonly EnkiTestWebApplicationFactory _factory;

    public TenantsEndpointSmokeTests(EnkiTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Tenants_AnonymousScheme_RoundTripsThroughEntirePipeline()
    {
        // Arrange — seed the master DB with a known tenant.
        await _factory.SeedMasterAsync(async db =>
        {
            // EnsureCreated runs in the seed helper; here we only Add.
            db.Tenants.Add(new Tenant("ACME", "Acme Corp")
            {
                Status = TenantStatus.Active,
            });
            await Task.CompletedTask;
        });

        var client = _factory.CreateClient();

        // Act — full HTTP roundtrip: routing → auth scheme (Test) →
        // EnkiApiScope policy → TenantsController.List → EF projection
        // → JSON serialisation → back to us.
        var response = await client.GetAsync("/tenants");

        // Assert — status, content type, parseable body, expected row.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tenants = await response.Content.ReadFromJsonAsync<List<TenantSummaryDto>>();
        Assert.NotNull(tenants);
        var acme = Assert.Single(tenants!);
        Assert.Equal("ACME",      acme.Code);
        Assert.Equal("Acme Corp", acme.Name);
        Assert.Equal("Active",    acme.Status);
    }

    [Fact]
    public async Task Get_Health_ReturnsOk_NoAuthRequired()
    {
        // /health is mapped without an [Authorize] policy. Smoke test
        // for the lighter end of the pipeline (no auth, no controller).
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ============================================================
    // Issue #23 — master-registry endpoints reachable for non-Active tenants
    // ============================================================

    [Fact]
    public async Task Reactivate_InactiveTenant_ReturnsNoContent_Issue23()
    {
        // Without [SkipTenantRouting] on TenantsController, this endpoint
        // is unreachable in production: TenantRoutingMiddleware sees the
        // tenant is Inactive (the precondition for Reactivate) and 404s
        // before the controller runs. The unit test
        // Reactivate_InactiveTenant_SetsActiveAndClearsDeactivatedAt passes
        // only because it bypasses the pipeline. This test exercises the
        // full pipeline end-to-end.
        var factory = new EnkiTestWebApplicationFactory();
        try
        {
            // EF InMemory doesn't auto-stamp RowVersion (no SQL Server
            // rowversion column); seed an explicit value so the
            // ApplyClientRowVersion check has something to match against.
            var rowVersionBytes = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 };
            await factory.SeedMasterAsync(async db =>
            {
                db.Tenants.Add(new Tenant("DEACT", "Deactivated Co")
                {
                    Status = TenantStatus.Inactive,
                    DeactivatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    RowVersion = rowVersionBytes,
                });
                await Task.CompletedTask;
            });

            var client = factory.CreateClient();
            var response = await client.PostAsJsonAsync(
                "/tenants/DEACT/reactivate",
                new { rowVersion = ConcurrencyHelper.EncodeRowVersion(rowVersionBytes) });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        finally
        {
            factory.Dispose();
        }
    }

    [Fact]
    public async Task Get_InactiveTenant_ReturnsOk_Issue23()
    {
        // Sibling of the reactivate case: master-registry GET must work
        // for non-Active tenants so admins can view detail / land on the
        // page after Deactivate. Pre-fix this returned 404 from the
        // middleware ("Tenant '…' was not found") — the bug Mike reported.
        var factory = new EnkiTestWebApplicationFactory();
        try
        {
            await factory.SeedMasterAsync(async db =>
            {
                db.Tenants.Add(new Tenant("DEACT2", "Already Deactivated")
                {
                    Status = TenantStatus.Inactive,
                    DeactivatedAt = DateTimeOffset.UtcNow.AddDays(-7),
                });
                await Task.CompletedTask;
            });

            var client = factory.CreateClient();
            var response = await client.GetAsync("/tenants/DEACT2");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<TenantDetailDto>();
            Assert.NotNull(dto);
            Assert.Equal("DEACT2", dto!.Code);
            Assert.Equal("Inactive", dto.Status);
        }
        finally
        {
            factory.Dispose();
        }
    }

    [Fact]
    public async Task TenantScoped_InactiveTenant_Still404s_RevocationStillWorks()
    {
        // Negative regression: the [SkipTenantRouting] opt-out must not
        // accidentally unrevoke tenant-scoped routes (jobs, runs, wells,
        // etc). An Inactive tenant must continue to 404 on those — that's
        // the whole point of the middleware's hard-revocation rule.
        var factory = new EnkiTestWebApplicationFactory();
        try
        {
            await factory.SeedMasterAsync(async db =>
            {
                var tenant = new Tenant("REVOKED", "Revoked Co")
                {
                    Status = TenantStatus.Inactive,
                    DeactivatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                };
                db.Tenants.Add(tenant);
                db.TenantDatabases.Add(new TenantDatabase(
                    tenant.Id, TenantDatabaseKind.Active,  "test-server", "Enki_REVOKED_Active"));
                db.TenantDatabases.Add(new TenantDatabase(
                    tenant.Id, TenantDatabaseKind.Archive, "test-server", "Enki_REVOKED_Archive"));
                await Task.CompletedTask;
            });

            var client = factory.CreateClient();
            var response = await client.GetAsync("/tenants/REVOKED/jobs");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            factory.Dispose();
        }
    }
}
