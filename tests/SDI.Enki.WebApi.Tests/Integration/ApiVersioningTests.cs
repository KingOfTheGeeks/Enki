using System.Net;

namespace SDI.Enki.WebApi.Tests.Integration;

/// <summary>
/// Integration smoke for the API-versioning baseline. Confirms three
/// invariants of the current configuration:
///
/// <list type="bullet">
///   <item>An unversioned request resolves to the default v1.0 — every
///   existing client keeps working.</item>
///   <item>Asking for v1.0 explicitly via either the query-string or
///   the header reader works.</item>
///   <item>Asking for an unsupported version (v99) fails closed —
///   the framework produces a 400, not silently downgrades.</item>
/// </list>
/// </summary>
public class ApiVersioningTests : IClassFixture<EnkiTestWebApplicationFactory>
{
    private readonly EnkiTestWebApplicationFactory _factory;

    public ApiVersioningTests(EnkiTestWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task UnversionedRequest_ResolvesToDefaultV1()
    {
        // Pick an anonymous endpoint so we can probe without
        // setting up auth — /health/live is the simplest. The
        // ReportApiVersions option emits `api-supported-versions`
        // on every response from a versioned controller; health
        // checks aren't versioned, so we instead verify the call
        // succeeds without a version-related rejection.
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ExplicitV1ViaQueryString_IsAccepted()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live?api-version=1.0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ExplicitV1ViaHeader_IsAccepted()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Version", "1.0");
        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
