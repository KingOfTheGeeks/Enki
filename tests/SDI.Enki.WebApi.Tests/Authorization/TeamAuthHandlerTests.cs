using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Core.Master.Users;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Identity;
using SDI.Enki.WebApi.Authorization;

namespace SDI.Enki.WebApi.Tests.Authorization;

/// <summary>
/// Decision-tree coverage for <see cref="TeamAuthHandler"/>. Exercises
/// every branch of the handler's logic against hand-crafted principals
/// and route values. Each test pins one branch — adding a new
/// requirement parameter or rule should land a new test here, not
/// modify an existing one.
///
/// <para>
/// The fixture uses an InMemory <see cref="EnkiMasterDbContext"/> for
/// the membership-lookup tests. Tenant lookups for Tenant-type users
/// also go through the same DbContext.
/// </para>
/// </summary>
public class TeamAuthHandlerTests
{
    // ---------- fixture helpers ----------

    private static EnkiMasterDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseInMemoryDatabase($"team-auth-{name}-{Guid.NewGuid():N}")
            .Options;
        return new EnkiMasterDbContext(opts);
    }

    private static IMemoryCache NewCache() =>
        new MemoryCache(new MemoryCacheOptions());

    private static TeamAuthHandler NewHandler(EnkiMasterDbContext db, IMemoryCache? cache = null) =>
        new(db, cache ?? NewCache(), new NoopAuditor(), NullLogger<TeamAuthHandler>.Instance);

    private sealed class NoopAuditor : IAuthzDenialAuditor
    {
        public Task RecordAsync(string policy, string? tenantCode, string actorSub, string? reason = null)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Build a principal with the given claims. <paramref name="sub"/>
    /// must be a Guid string — the handler parses it as the user's
    /// AspNetUsers Id.
    /// </summary>
    private static ClaimsPrincipal Principal(
        Guid sub,
        bool isAdmin = false,
        TeamSubtype? subtype = null,
        UserType? userType = null,
        Guid? tenantId = null,
        string? capability = null)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim("sub", sub.ToString("D")));
        if (isAdmin)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, AuthConstants.EnkiAdminRole));
            identity.AddClaim(new Claim("role", AuthConstants.EnkiAdminRole));
        }
        if (userType is not null)
            identity.AddClaim(new Claim(AuthConstants.UserTypeClaim, userType.Name));
        if (subtype is not null)
            identity.AddClaim(new Claim(AuthConstants.TeamSubtypeClaim, subtype.Name));
        if (tenantId is not null)
            identity.AddClaim(new Claim(AuthConstants.TenantIdClaim, tenantId.Value.ToString("D")));
        if (!string.IsNullOrEmpty(capability))
            identity.AddClaim(new Claim(EnkiClaimTypes.Capability, capability));
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Build an HttpContext that exposes <c>{tenantCode}</c> as a route
    /// value so <c>TenantAuthExtractor.TryExtract</c> finds it.
    /// </summary>
    private static HttpContext HttpFor(string? tenantCode = null)
    {
        var http = new DefaultHttpContext();
        if (!string.IsNullOrEmpty(tenantCode))
            http.Request.RouteValues["tenantCode"] = tenantCode;
        return http;
    }

    private static AuthorizationHandlerContext Context(
        TeamAuthRequirement req, ClaimsPrincipal user, HttpContext http)
        => new(new[] { req }, user, http);

    private static (Tenant tenant, User user) SeedMember(EnkiMasterDbContext db, string code, Guid identityId)
    {
        var tenant = new Tenant(code, $"{code} Corp") { Status = TenantStatus.Active };
        db.Tenants.Add(tenant);
        var user = new User($"User-{identityId:N}", identityId);
        db.Users.Add(user);
        db.TenantUsers.Add(new TenantUser(tenant.Id, user.Id));
        db.SaveChanges();
        return (tenant, user);
    }

    private static Tenant SeedTenant(EnkiMasterDbContext db, string code, Guid? id = null)
    {
        var tenant = new Tenant(code, $"{code} Corp") { Status = TenantStatus.Active };
        if (id is not null) tenant.Id = id.Value;
        db.Tenants.Add(tenant);
        db.SaveChanges();
        return tenant;
    }

    // ---------- step 1: RequireAdmin ----------

    [Fact]
    public async Task RequireAdmin_WithAdmin_Succeeds()
    {
        await using var db = NewDb();
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(RequireAdmin: true),
            Principal(Guid.NewGuid(), isAdmin: true),
            HttpFor());
        await sut.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task RequireAdmin_WithoutAdmin_Denied()
    {
        await using var db = NewDb();
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(RequireAdmin: true),
            Principal(Guid.NewGuid(), isAdmin: false, subtype: TeamSubtype.Supervisor),
            HttpFor());
        await sut.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    // ---------- step 2: admin bypass ----------

    [Fact]
    public async Task Admin_AlwaysSucceeds_OnAnyMasterPolicy()
    {
        await using var db = NewDb();
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Supervisor),
            Principal(Guid.NewGuid(), isAdmin: true),
            HttpFor());
        await sut.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task Admin_AlwaysSucceeds_OnAnyTenantPolicy()
    {
        // Admin doesn't need to be a member.
        await using var db = NewDb();
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Supervisor, TenantScoped: true),
            Principal(Guid.NewGuid(), isAdmin: true),
            HttpFor("ACME"));
        await sut.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    // ---------- step 3: Tenant-type principal ----------

    [Fact]
    public async Task TenantUser_OnMasterEndpoint_Denied()
    {
        await using var db = NewDb();
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Office),   // master, not TenantScoped
            Principal(Guid.NewGuid(), userType: UserType.Tenant, tenantId: Guid.NewGuid()),
            HttpFor());
        await sut.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task TenantUser_OnTheirOwnTenant_FieldEquivalentSucceeds()
    {
        await using var db = NewDb();
        var tenant = SeedTenant(db, "ACME");
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(TenantScoped: true),                   // no MinimumSubtype = Field-equivalent
            Principal(Guid.NewGuid(), userType: UserType.Tenant, tenantId: tenant.Id),
            HttpFor("ACME"));
        await sut.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task TenantUser_OnDifferentTenant_Denied()
    {
        await using var db = NewDb();
        var bound  = SeedTenant(db, "ACME");
        SeedTenant(db, "BAKKEN");
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(TenantScoped: true),
            Principal(Guid.NewGuid(), userType: UserType.Tenant, tenantId: bound.Id),
            HttpFor("BAKKEN"));
        await sut.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task TenantUser_TryingToWrite_Denied()
    {
        // Tenant users are pinned to Field-equivalent (no MinimumSubtype
        // policies). Office-tier ops on their own tenant must deny.
        await using var db = NewDb();
        var tenant = SeedTenant(db, "ACME");
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Office, TenantScoped: true),
            Principal(Guid.NewGuid(), userType: UserType.Tenant, tenantId: tenant.Id),
            HttpFor("ACME"));
        await sut.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task TenantUser_MissingTenantIdClaim_Denied()
    {
        await using var db = NewDb();
        SeedTenant(db, "ACME");
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(TenantScoped: true),
            Principal(Guid.NewGuid(), userType: UserType.Tenant, tenantId: null),
            HttpFor("ACME"));
        await sut.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task TenantUser_BoundToDeletedTenant_Denied()
    {
        await using var db = NewDb();
        SeedTenant(db, "ACME");
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(TenantScoped: true),
            Principal(Guid.NewGuid(), userType: UserType.Tenant, tenantId: Guid.NewGuid()), // unknown id
            HttpFor("ACME"));
        await sut.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    // ---------- step 4: Team membership gate ----------

    [Fact]
    public async Task TeamUser_Member_NoSubtypeGate_Succeeds()
    {
        await using var db = NewDb();
        var identityId = Guid.NewGuid();
        SeedMember(db, "ACME", identityId);
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(TenantScoped: true),
            Principal(identityId, subtype: TeamSubtype.Field),
            HttpFor("ACME"));
        await sut.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task TeamUser_NotMember_Denied()
    {
        await using var db = NewDb();
        SeedTenant(db, "ACME");
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(TenantScoped: true),
            Principal(Guid.NewGuid(), subtype: TeamSubtype.Office),  // not a member of ACME
            HttpFor("ACME"));
        await sut.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    // ---------- steps 5-6: subtype gate ----------

    [Fact]
    public async Task TeamUser_Field_OnOfficeGate_Denied()
    {
        await using var db = NewDb();
        var identityId = Guid.NewGuid();
        SeedMember(db, "ACME", identityId);
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Office, TenantScoped: true),
            Principal(identityId, subtype: TeamSubtype.Field),
            HttpFor("ACME"));
        await sut.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task TeamUser_Office_OnOfficeGate_Succeeds()
    {
        await using var db = NewDb();
        var identityId = Guid.NewGuid();
        SeedMember(db, "ACME", identityId);
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Office, TenantScoped: true),
            Principal(identityId, subtype: TeamSubtype.Office),
            HttpFor("ACME"));
        await sut.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task TeamUser_Supervisor_OnOfficeGate_Succeeds()
    {
        // Higher subtype satisfies the lower minimum.
        await using var db = NewDb();
        var identityId = Guid.NewGuid();
        SeedMember(db, "ACME", identityId);
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Office, TenantScoped: true),
            Principal(identityId, subtype: TeamSubtype.Supervisor),
            HttpFor("ACME"));
        await sut.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task TeamUser_Office_OnSupervisorGate_Denied()
    {
        await using var db = NewDb();
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Supervisor),  // master scope
            Principal(Guid.NewGuid(), subtype: TeamSubtype.Office),
            HttpFor());
        await sut.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    // ---------- step 7: capability OR ----------

    [Fact]
    public async Task Office_WithLicensingCapability_PassesLicensingGate()
    {
        // Office is below Supervisor on the subtype gate but holding the
        // licensing capability satisfies the OR clause.
        await using var db = NewDb();
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(
                MinimumSubtype:    TeamSubtype.Supervisor,
                GrantingCapability: EnkiCapabilities.Licensing),
            Principal(Guid.NewGuid(),
                subtype:    TeamSubtype.Office,
                capability: EnkiCapabilities.Licensing),
            HttpFor());
        await sut.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    [Fact]
    public async Task Office_WithoutLicensingCapability_LicensingGateDenies()
    {
        await using var db = NewDb();
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(
                MinimumSubtype:    TeamSubtype.Supervisor,
                GrantingCapability: EnkiCapabilities.Licensing),
            Principal(Guid.NewGuid(), subtype: TeamSubtype.Office),
            HttpFor());
        await sut.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task Field_WithLicensingCapability_PassesLicensingGate()
    {
        // Capability is the orthogonal grant — Field with Licensing capability
        // is also OK on the licensing endpoint.
        await using var db = NewDb();
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(
                MinimumSubtype:    TeamSubtype.Supervisor,
                GrantingCapability: EnkiCapabilities.Licensing),
            Principal(Guid.NewGuid(),
                subtype:    TeamSubtype.Field,
                capability: EnkiCapabilities.Licensing),
            HttpFor());
        await sut.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }

    // ---------- fail-safe paths ----------

    [Fact]
    public async Task TeamUser_NoSubtypeClaim_OnSubtypeGate_Denied()
    {
        // Data drift: pre-classification user has no team_subtype claim.
        // Fail-safe: missing subtype never elevates.
        await using var db = NewDb();
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(MinimumSubtype: TeamSubtype.Office),
            Principal(Guid.NewGuid(), subtype: null),
            HttpFor());
        await sut.HandleAsync(ctx);
        Assert.False(ctx.HasSucceeded);
    }

    [Fact]
    public async Task TeamUser_NoSubtypeClaim_OnReadGate_Succeeds()
    {
        // Pre-classification users CAN still hit reads — no subtype gate
        // applies, only membership.
        await using var db = NewDb();
        var identityId = Guid.NewGuid();
        SeedMember(db, "ACME", identityId);
        var sut = NewHandler(db);
        var ctx = Context(
            new TeamAuthRequirement(TenantScoped: true),
            Principal(identityId, subtype: null),
            HttpFor("ACME"));
        await sut.HandleAsync(ctx);
        Assert.True(ctx.HasSucceeded);
    }
}
