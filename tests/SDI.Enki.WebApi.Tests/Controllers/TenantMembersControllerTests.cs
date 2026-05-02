using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Core.Master.Users;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Tenants;
using SDI.Enki.WebApi.Controllers;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="TenantMembersController"/>.
/// Pins the three mutation paths (List / Add / Remove) + the
/// precondition errors (unknown tenant, unknown user, duplicate add).
/// Auth-policy enforcement happens upstream and is exercised by the
/// integration tests; these direct invocations bypass the policy gate
/// to test the action body.
///
/// <para>
/// <b>Per-tenant Role retired (2026-05-01).</b> The previous SetRole
/// endpoint and Add-with-role tests are gone — Add now carries only
/// the UserId, no role.
/// </para>
/// </summary>
public class TenantMembersControllerTests
{
    // ---------- fixture helpers ----------

    private static EnkiMasterDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseInMemoryDatabase($"tenant-members-{name}-{Guid.NewGuid():N}")
            .Options;
        return new EnkiMasterDbContext(opts);
    }

    private static TenantMembersController NewController(EnkiMasterDbContext db)
    {
        // Real-but-empty IMemoryCache so the controller's membership-
        // cache bust on add/remove lands harmlessly. The handler-side
        // membership cache is invalidated by the same key shape.
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());

        return new TenantMembersController(db, cache)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
    }

    private static Tenant SeedTenant(EnkiMasterDbContext db, string code = "ACME")
    {
        var tenant = new Tenant(code, $"{code} Corp") { Status = TenantStatus.Active };
        db.Tenants.Add(tenant);
        db.SaveChanges();
        return tenant;
    }

    private static User SeedUser(EnkiMasterDbContext db, string name)
    {
        var user = new User(name, Guid.NewGuid());
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static void SeedMembership(EnkiMasterDbContext db, Tenant tenant, User user)
    {
        db.TenantUsers.Add(new TenantUser(tenant.Id, user.Id));
        db.SaveChanges();
    }

    private static void AssertProblem(IActionResult result, int expectedStatus)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, obj.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(obj.Value);
        Assert.Equal(expectedStatus, problem.Status);
    }

    // ---------- list ----------

    [Fact]
    public async Task List_UnknownTenant_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var result = await sut.List("DOES-NOT-EXIST", CancellationToken.None);

        AssertProblem(result, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task List_NoMembers_ReturnsEmpty()
    {
        await using var db = NewDb();
        SeedTenant(db, "ACME");

        var sut = NewController(db);
        var result = await sut.List("ACME", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var rows = Assert.IsAssignableFrom<IEnumerable<TenantMemberDto>>(ok.Value);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task List_WithMembers_ReturnsThemOrderedByName()
    {
        await using var db = NewDb();
        var tenant = SeedTenant(db, "ACME");
        var zara   = SeedUser(db, "Zara Yusuf");
        var adam   = SeedUser(db, "Adam Karabasz");
        var mike   = SeedUser(db, "Mike King");
        SeedMembership(db, tenant, zara);
        SeedMembership(db, tenant, adam);
        SeedMembership(db, tenant, mike);

        var sut = NewController(db);
        var result = await sut.List("ACME", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var rows = Assert.IsAssignableFrom<IEnumerable<TenantMemberDto>>(ok.Value);
        var list = rows.ToList();

        Assert.Equal(3, list.Count);
        Assert.Equal(
            new[] { "Adam Karabasz", "Mike King", "Zara Yusuf" },
            list.Select(r => r.Username));
    }

    [Fact]
    public async Task List_OnlyReturnsMembersOfTheRequestedTenant()
    {
        // Cross-tenant guard: a user who's a member of ACME shouldn't
        // appear in BAKKEN's list, even on an in-memory DB where the
        // join could in principle leak rows.
        await using var db = NewDb();
        var acme   = SeedTenant(db, "ACME");
        var bakken = SeedTenant(db, "BAKKEN");
        var alice  = SeedUser(db, "Alice");
        var bob    = SeedUser(db, "Bob");
        SeedMembership(db, acme,   alice);
        SeedMembership(db, bakken, bob);

        var sut = NewController(db);
        var bakkenResult = await sut.List("BAKKEN", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(bakkenResult);
        var rows = Assert.IsAssignableFrom<IEnumerable<TenantMemberDto>>(ok.Value).ToList();
        Assert.Single(rows);
        Assert.Equal("Bob", rows[0].Username);
    }

    // ---------- add ----------

    [Fact]
    public async Task Add_UnknownTenant_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        var user = SeedUser(db, "Mike King");

        var sut = NewController(db);
        var result = await sut.Add("DOES-NOT-EXIST",
            new AddTenantMemberDto(user.Id),
            CancellationToken.None);

        AssertProblem(result, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task Add_UnknownUser_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        SeedTenant(db, "ACME");

        var sut = NewController(db);
        var result = await sut.Add("ACME",
            new AddTenantMemberDto(Guid.NewGuid()),
            CancellationToken.None);

        AssertProblem(result, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task Add_AlreadyMember_ReturnsConflictProblem()
    {
        await using var db = NewDb();
        var tenant = SeedTenant(db, "ACME");
        var user   = SeedUser(db, "Mike King");
        SeedMembership(db, tenant, user);

        var sut = NewController(db);
        var result = await sut.Add("ACME",
            new AddTenantMemberDto(user.Id),
            CancellationToken.None);

        AssertProblem(result, StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Add_HappyPath_PersistsMembership()
    {
        await using var db = NewDb();
        var tenant = SeedTenant(db, "ACME");
        var user   = SeedUser(db, "Mike King");

        var sut = NewController(db);
        var result = await sut.Add("ACME",
            new AddTenantMemberDto(user.Id),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var membership = await db.TenantUsers.SingleAsync();
        Assert.Equal(tenant.Id, membership.TenantId);
        Assert.Equal(user.Id, membership.UserId);
    }

    // ---------- remove ----------

    [Fact]
    public async Task Remove_UnknownTenant_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var result = await sut.Remove("DOES-NOT-EXIST", Guid.NewGuid(), CancellationToken.None);

        AssertProblem(result, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task Remove_UnknownMembership_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        SeedTenant(db, "ACME");

        var sut = NewController(db);
        var result = await sut.Remove("ACME", Guid.NewGuid(), CancellationToken.None);

        AssertProblem(result, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task Remove_HappyPath_DeletesMembershipOnly()
    {
        // Removing a membership should NOT cascade to the master User
        // — Mike resigning from ACME doesn't blow away his account.
        await using var db = NewDb();
        var tenant = SeedTenant(db, "ACME");
        var user   = SeedUser(db, "Mike King");
        SeedMembership(db, tenant, user);

        var sut = NewController(db);
        var result = await sut.Remove("ACME", user.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(db.TenantUsers.ToList());
        Assert.Single(db.Users.ToList());   // master User intact
    }
}
