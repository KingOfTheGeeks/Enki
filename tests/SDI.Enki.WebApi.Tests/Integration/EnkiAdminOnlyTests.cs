using System.Net;
using System.Net.Http.Json;
using SDI.Enki.Shared.Settings;

namespace SDI.Enki.WebApi.Tests.Integration;

/// <summary>
/// Pins the <c>EnkiAdminOnly</c> policy on the routes that opted into
/// it. Mirrors <see cref="TenantAuthorizationTests"/> in shape: every
/// system-admin endpoint goes through the same matrix (anon → 401,
/// non-admin authenticated → 403, admin → 200) so a future controller
/// that ships under the wrong policy gets caught here, not in prod.
///
/// Net effect of this test class is: if anyone replaces
/// <c>[Authorize(Policy = EnkiAdminOnly)]</c> with the looser
/// <c>EnkiApiScope</c> the way <c>SystemSettingsController</c>
/// originally did, the build fails on PR.
/// </summary>
public class EnkiAdminOnlyTests : IClassFixture<EnkiTestWebApplicationFactory>
{
    private readonly EnkiTestWebApplicationFactory _factory;

    public EnkiAdminOnlyTests(EnkiTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public static IEnumerable<object[]> AdminOnlyRoutes => new[]
    {
        new object[] { "/admin/settings" },
    };

    [Theory]
    [MemberData(nameof(AdminOnlyRoutes))]
    public async Task Anonymous_GetsUnauthorized(string route)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "1");

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyRoutes))]
    public async Task NonAdminAuthenticated_GetsForbidden(string route)
    {
        var client = _factory.CreateClient();
        // Authenticated user but explicitly NOT admin.
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader,   Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.AdminHeader, "false");

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyRoutes))]
    public async Task Admin_Succeeds(string route)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader,   Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.AdminHeader, "true");

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Sanity: the body parses as the expected DTO. Not retesting
        // the controller's projection — just confirming the policy let
        // the request reach the handler.
        var body = await response.Content.ReadFromJsonAsync<List<SystemSettingDto>>();
        Assert.NotNull(body);
    }
}
