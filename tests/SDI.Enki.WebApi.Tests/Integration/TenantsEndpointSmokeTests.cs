using System.Net;
using System.Net.Http.Json;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Shared.Tenants;

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
}
