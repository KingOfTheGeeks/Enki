using System.Net;

namespace SDI.Enki.WebApi.Tests.Integration;

/// <summary>
/// Integration smoke for the health-check endpoints. The HTTP shape
/// matters for orchestrators (k8s, load balancers) — they probe by
/// status code, not body — so the assertions here pin the exact
/// status response for each tag-filter.
///
/// <para>
/// All three endpoints must be anonymous: orchestrator probes never
/// carry tokens, and a 401 from /health/live would soft-kill a healthy
/// pod. The factory's TestAuthHandler is configured to surface as
/// anonymous when no <c>X-Test-Sub</c> header is supplied, which is
/// exactly the probe path.
/// </para>
/// </summary>
public class HealthCheckEndpointTests : IClassFixture<EnkiTestWebApplicationFactory>
{
    private readonly EnkiTestWebApplicationFactory _factory;

    public HealthCheckEndpointTests(EnkiTestWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Health_Live_ReturnsHealthy_AnonymousAccess()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");

        // Liveness is the "is the process alive" probe — must NOT
        // depend on external services. Self-check is constant-Healthy;
        // anonymous and unauthenticated.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task Health_Ready_ReturnsHealthy_WhenDbIsReachable()
    {
        // Readiness checks the master-DB. The factory swaps the master
        // DbContext to InMemory which is always reachable, so the
        // probe should return Healthy.
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task Health_AggregateRoot_ReturnsHealthy()
    {
        // The unfiltered /health endpoint runs every check —
        // self + master-db. With both healthy the aggregate is
        // Healthy.
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
    }
}
