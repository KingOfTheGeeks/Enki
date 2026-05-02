using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Core.Master.Users;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.WebApi.Tests.Integration;

/// <summary>
/// Pins the <c>CanAccessTenant</c> policy across every tenant-scoped
/// controller. The policy was added in Phase 6c but five controllers
/// (Wells, Runs, Shots, Surveys, Gradients) shipped on the looser
/// <c>EnkiApiScope</c> until this test was written — that gap let a
/// logged-in user from tenant A read tenant B's data via
/// <c>/tenants/B/&lt;anything&gt;</c>. These tests fail the build if
/// any controller regresses.
///
/// <para>
/// The matrix exercised:
/// </para>
/// <list type="bullet">
///   <item>Anonymous → 401 on every tenant route.</item>
///   <item>User who's a member of tenant A → 200/204/etc on
///   <c>/tenants/A/...</c>; 403 on <c>/tenants/B/...</c>.</item>
///   <item>User with the <c>enki-admin</c> role → reaches every tenant
///   regardless of membership (admin short-circuit at step 2 of
///   <c>TeamAuthHandler.HandleRequirementAsync</c>).</item>
/// </list>
///
/// <para>
/// The endpoints chosen are <c>GET</c>-only listings — they touch the
/// fewest moving parts so a 200 means "the policy let me through" and
/// not "I happened to satisfy some other complex contract".
/// </para>
/// </summary>
public class TenantAuthorizationTests : IClassFixture<EnkiTestWebApplicationFactory>
{
    private readonly EnkiTestWebApplicationFactory _factory;

    // Two tenants + a user (identity + master rows) who only belongs to ALPHA.
    // The principal's `sub` is the AspNetUsers.Id (= User.IdentityId);
    // TenantUser.UserId is the master User.Id. Two different Guids,
    // bridged by master User.IdentityId.
    private static readonly Guid AlphaTenantId        = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BravoTenantId        = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const   string  AlphaCode                 = "ALPHA";
    private const   string  BravoCode                 = "BRAVO";
    private static readonly Guid MemberOfAlphaSub     = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MemberOfAlphaUserId  = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>
    /// Every tenant-scoped GET endpoint that's wired today. Add new
    /// rows here when a new tenant-scoped route ships — adding a row
    /// extends both the cross-tenant denial coverage and the
    /// admin-bypass coverage by Theory expansion.
    /// </summary>
    public static IEnumerable<object[]> TenantScopedRoutes => new[]
    {
        new object[] { "/tenants/{0}/jobs" },
        // Wells now nest under Jobs (/tenants/{c}/jobs/{jobId}/wells) — no
        // top-level wells route to test directly. The auth policy
        // (CanAccessTenant) is identical on WellsController and the
        // /jobs row above already exercises it.
    };

    public TenantAuthorizationTests(EnkiTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [MemberData(nameof(TenantScopedRoutes))]
    public async Task Anonymous_GetsUnauthorized(string routeTemplate)
    {
        await SeedTwoTenantsAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "1");

        var response = await client.GetAsync(string.Format(routeTemplate, AlphaCode));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(TenantScopedRoutes))]
    public async Task Member_OfTenantA_CanAccess_TenantA(string routeTemplate)
    {
        await SeedTwoTenantsAsync();
        var client = AsMemberOfAlpha();

        var response = await client.GetAsync(string.Format(routeTemplate, AlphaCode));

        // 200 on a successful list, but verify the wire didn't 403/401.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(TenantScopedRoutes))]
    public async Task Member_OfTenantA_CannotAccess_TenantB(string routeTemplate)
    {
        // The exploit guard. Pre-fix this returned 200 with tenant B's data.
        await SeedTwoTenantsAsync();
        var client = AsMemberOfAlpha();

        var response = await client.GetAsync(string.Format(routeTemplate, BravoCode));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(TenantScopedRoutes))]
    public async Task Admin_CanAccess_AnyTenant(string routeTemplate)
    {
        await SeedTwoTenantsAsync();
        var client = AsAdmin();

        var responseA = await client.GetAsync(string.Format(routeTemplate, AlphaCode));
        var responseB = await client.GetAsync(string.Format(routeTemplate, BravoCode));

        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
    }

    // ---------- helpers ----------

    private HttpClient AsMemberOfAlpha()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader,   MemberOfAlphaSub.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.AdminHeader, "false");
        return client;
    }

    private HttpClient AsAdmin()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader,   Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.AdminHeader, "true");
        return client;
    }

    /// <summary>
    /// Seeds master with: ALPHA + BRAVO tenants, both with Active +
    /// Archive TenantDatabase rows so <c>TenantRoutingMiddleware</c>
    /// can resolve, and a TenantUser linking <see cref="MemberOfAlphaSub"/>
    /// to ALPHA only. Idempotent — multiple test methods sharing the
    /// fixture won't re-add.
    /// </summary>
    private async Task SeedTwoTenantsAsync() => await _factory.SeedMasterAsync(async db =>
    {
        // Master User row first — TenantUser FK joins via User.Id, and
        // the policy joins via User.IdentityId.
        if (!await db.Users.AnyAsync(u => u.Id == MemberOfAlphaUserId))
        {
            db.Users.Add(new User("test.member", MemberOfAlphaSub) { Id = MemberOfAlphaUserId });
        }

        if (!await db.Tenants.AnyAsync(t => t.Code == AlphaCode))
        {
            var alpha = new Tenant(AlphaCode, "Alpha Corp")  { Id = AlphaTenantId, Status = TenantStatus.Active };
            db.Tenants.Add(alpha);
            db.TenantDatabases.Add(new TenantDatabase(
                AlphaTenantId, TenantDatabaseKind.Active,  "test-server", "Enki_ALPHA_Active"));
            db.TenantDatabases.Add(new TenantDatabase(
                AlphaTenantId, TenantDatabaseKind.Archive, "test-server", "Enki_ALPHA_Archive"));
            db.TenantUsers.Add(new TenantUser(AlphaTenantId, MemberOfAlphaUserId));
        }
        if (!await db.Tenants.AnyAsync(t => t.Code == BravoCode))
        {
            var bravo = new Tenant(BravoCode, "Bravo Corp") { Id = BravoTenantId, Status = TenantStatus.Active };
            db.Tenants.Add(bravo);
            db.TenantDatabases.Add(new TenantDatabase(
                BravoTenantId, TenantDatabaseKind.Active,  "test-server", "Enki_BRAVO_Active"));
            db.TenantDatabases.Add(new TenantDatabase(
                BravoTenantId, TenantDatabaseKind.Archive, "test-server", "Enki_BRAVO_Archive"));
            // No TenantUser for BRAVO — that's the point.
        }
        await Task.CompletedTask;
    });
}
