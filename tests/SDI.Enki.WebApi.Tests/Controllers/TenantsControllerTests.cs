using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning;
using SDI.Enki.Infrastructure.Provisioning.Models;
using SDI.Enki.Shared.Concurrency;
using SDI.Enki.Shared.Tenants;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Direct controller tests against an in-memory <see cref="EnkiMasterDbContext"/>.
/// These exercise the controller's action logic and EF query shapes without
/// spinning up the full HTTP pipeline (which would need OpenIddict + auth
/// test harnesses). The controller throws <see cref="EnkiException"/>
/// subclasses for error paths; in production the global exception handler
/// maps those to ProblemDetails, which is a separate concern verified by
/// the handler's own tests.
///
/// Each test gets a fresh database (unique InMemory name) so parallel xunit
/// execution doesn't cross-pollute state.
/// </summary>
public class TenantsControllerTests
{
    // ---------- fixture helpers ----------

    private static EnkiMasterDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseInMemoryDatabase($"tenants-{name}-{Guid.NewGuid():N}")
            .Options;
        return new EnkiMasterDbContext(opts);
    }

    private static TenantsController NewController(
        EnkiMasterDbContext db,
        ITenantProvisioningService? provisioning = null)
    {
        // IMemoryCache is part of the controller surface so deactivate /
        // reactivate can bust the resolved-tenant cache. The unit-test
        // path just needs a real-but-empty cache; no need for Moq.
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());

        var controller = new TenantsController(
            db,
            provisioning ?? new FakeTenantProvisioningService(),
            cache);
        // Give the controller a real HttpContext so EnkiResults helpers
        // have something to read Request.Path and TraceIdentifier from.
        // The User principal carries an enki-admin role claim so the
        // membership filter on List doesn't trim the test seeds — these
        // tests cover action logic + EF query shapes, NOT the authz
        // boundary (which Isolation.Tests + the policy handlers cover).
        var identity = new ClaimsIdentity(
            [new Claim("role", "enki-admin")],
            authenticationType: "Test",
            nameType: "name",
            roleType: "role");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        return controller;
    }

    private static void AssertProblem(IActionResult result, int expectedStatus, string expectedTypeSuffix)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, obj.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(obj.Value);
        Assert.Equal(expectedStatus, problem.Status);
        Assert.EndsWith(expectedTypeSuffix, problem.Type);
        Assert.NotNull(problem.Extensions["traceId"]);
    }

    private static Tenant SeedTenant(
        EnkiMasterDbContext db,
        string code = "ACME",
        string name = "Acme Corp",
        TenantStatus? status = null,
        TenantDatabaseKind? database = null)
    {
        var tenant = new Tenant(code, name)
        {
            Status     = status ?? TenantStatus.Active,
            RowVersion = TestRowVersionBytes,
        };
        db.Tenants.Add(tenant);

        // A tenant normally has both databases; give it the Active by default
        // so the Get-by-code projection has something to report.
        if (database != TenantDatabaseKind.Archive)
        {
            db.TenantDatabases.Add(new TenantDatabase(
                tenant.Id, TenantDatabaseKind.Active,
                "test-server", $"Enki_{code}_Active")
            {
                SchemaVersion = "20260101000000_Initial",
            });
        }

        db.SaveChanges();
        return tenant;
    }

    private static readonly byte[] TestRowVersionBytes = [0, 0, 0, 0, 0, 0, 0, 1];
    private static readonly string TestRowVersion = Convert.ToBase64String(TestRowVersionBytes);

    // ============================================================
    // List
    // ============================================================

    [Fact]
    public async Task List_NoTenants_ReturnsEmpty()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var result = await sut.List(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task List_WithTenants_ReturnsSummariesOrderedByCode()
    {
        await using var db = NewDb();
        SeedTenant(db, code: "ZEBRA", name: "Zebra Ltd");
        SeedTenant(db, code: "ACME",  name: "Acme Corp");
        SeedTenant(db, code: "MIKE",  name: "Mike Inc");

        var sut = NewController(db);
        var result = (await sut.List(CancellationToken.None)).ToList();

        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { "ACME", "MIKE", "ZEBRA" }, result.Select(t => t.Code));
        Assert.All(result, t => Assert.False(string.IsNullOrEmpty(t.Name)));
    }

    // ============================================================
    // Get (by code)
    // ============================================================

    [Fact]
    public async Task Get_KnownCode_ReturnsDetail()
    {
        await using var db = NewDb();
        SeedTenant(db, code: "ACME", name: "Acme Corp");
        var sut = NewController(db);

        var result = await sut.Get("ACME", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<TenantDetailDto>(ok.Value);
        Assert.Equal("ACME", dto.Code);
        Assert.Equal("Acme Corp", dto.Name);
        Assert.Equal("Active", dto.Status);
        Assert.Equal("Enki_ACME_Active", dto.ActiveDatabaseName);
        Assert.Equal("20260101000000_Initial", dto.SchemaVersion);
    }

    [Fact]
    public async Task Get_UnknownCode_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var result = await sut.Get("NOPE", CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
        var problem = (ProblemDetails)((ObjectResult)result).Value!;
        Assert.Equal("Tenant", problem.Extensions["entityKind"]);
        Assert.Equal("NOPE",   problem.Extensions["entityKey"]);
    }

    [Fact]
    public async Task Get_TenantWithoutActiveDatabase_ReturnsOk()
    {
        await using var db = NewDb();
        var t = new Tenant("SOLO", "Solo Co");
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        var sut = NewController(db);

        var result = await sut.Get("SOLO", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<TenantDetailDto>(ok.Value);
        Assert.Equal(string.Empty, dto.ActiveDatabaseName);
        Assert.Null(dto.SchemaVersion);
    }

    // ============================================================
    // Provision (create)
    // ============================================================

    [Fact]
    public async Task Provision_ValidRequest_CallsServiceAndReturnsCreated()
    {
        await using var db = NewDb();
        var fake = new FakeTenantProvisioningService();
        var sut = NewController(db, fake);

        var dto = new ProvisionTenantDto(
            Code: "NEWCO",
            Name: "New Company",
            DisplayName: "NewCo Inc.",
            ContactEmail: "admin@newco.example",
            Notes: "first tenant");

        var result = await sut.Provision(dto, CancellationToken.None);

        Assert.Equal(1, fake.CallCount);
        Assert.Equal("NEWCO", fake.LastRequest?.Code);
        Assert.Equal("New Company", fake.LastRequest?.Name);
        Assert.Equal("NewCo Inc.", fake.LastRequest?.DisplayName);
        Assert.Equal("admin@newco.example", fake.LastRequest?.ContactEmail);
        Assert.Equal("first tenant", fake.LastRequest?.Notes);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(TenantsController.Get), created.ActionName);
        Assert.Equal("NEWCO", created.RouteValues?["tenantCode"]);
        var body = Assert.IsType<ProvisionTenantResult>(created.Value);
        Assert.Equal("NEWCO", body.Code);
    }

    [Fact]
    public async Task Provision_ServiceThrows_PropagatesEnkiException()
    {
        await using var db = NewDb();
        var partialId = Guid.NewGuid();
        var fake = new FakeTenantProvisioningService
        {
            ThrowOnProvision = new TenantProvisioningException(
                "database already exists", partialId),
        };
        var sut = NewController(db, fake);

        // TenantProvisioningException is an EnkiException (400) and propagates
        // up to the global handler in production. In tests we assert the throw.
        var ex = await Assert.ThrowsAsync<TenantProvisioningException>(() =>
            sut.Provision(new ProvisionTenantDto("DUPE", "Dupe Co"), CancellationToken.None));

        Assert.Equal(400, ex.HttpStatusCode);
        Assert.Equal("database already exists", ex.Message);
        Assert.Equal(partialId, ex.PartialTenantId);
        Assert.Equal(partialId, ex.Extensions["partialTenantId"]);
    }

    [Fact]
    public async Task Provision_BustsNegativeCacheEntry_LeftByPreSubmitProbe_Issue21()
    {
        // Regression for #21: TenantCreate.razor probes GET /tenants/{code}
        // before POSTing to detect "code already taken". For a free code,
        // that probe returns 404 — and TenantRoutingMiddleware caches
        // `null` for the code for 5 minutes. Without busting the cache
        // here, the post-Provision redirect to /tenants/{code}/jobs hits
        // the stale negative entry and 404s with "Tenant '…' was not found",
        // even though the master rows + physical DBs all exist.
        await using var db = NewDb();
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());

        // Simulate the pre-submit probe poisoning the cache for "NEWCO".
        // The middleware uses the same Set(key, null, ttl) shape via
        // GetOrCreateAsync when its factory returns null.
        var cacheKey = SDI.Enki.WebApi.Multitenancy.TenantRoutingMiddleware.CacheKeyFor("NEWCO");
        cache.Set<object?>(cacheKey, null, TimeSpan.FromMinutes(5));
        Assert.True(cache.TryGetValue(cacheKey, out _),
            "sanity check: the simulated probe should have populated the cache");

        var sut = new TenantsController(db, new FakeTenantProvisioningService(), cache);
        var identity = new ClaimsIdentity(
            [new Claim("role", "enki-admin")],
            authenticationType: "Test", nameType: "name", roleType: "role");
        sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };

        var result = await sut.Provision(
            new ProvisionTenantDto(Code: "NEWCO", Name: "New Company"),
            CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result);
        Assert.False(cache.TryGetValue(cacheKey, out _),
            "Provision must remove the stale cache entry so the redirect to " +
            "/tenants/NEWCO/jobs sees the freshly-provisioned tenant.");
    }

    // ============================================================
    // Update
    // ============================================================

    [Fact]
    public async Task Update_ValidRequest_UpdatesMutableFields()
    {
        await using var db = NewDb();
        SeedTenant(db, code: "ACME", name: "Old Name");
        var sut = NewController(db);

        var dto = new UpdateTenantDto(
            Name: "New Name",
            DisplayName: "New Display",
            ContactEmail: "ops@acme.example",
            Notes: "updated",
            RowVersion: TestRowVersion);

        var result = await sut.Update("ACME", dto, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Code == "ACME");
        Assert.Equal("New Name", reloaded.Name);
        Assert.Equal("New Display", reloaded.DisplayName);
        Assert.Equal("ops@acme.example", reloaded.ContactEmail);
        Assert.Equal("updated", reloaded.Notes);
        // UpdatedAt / UpdatedBy are stamped by the audit interceptor on Modified.
        Assert.NotNull(reloaded.UpdatedAt);
        Assert.True(reloaded.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal("system", reloaded.UpdatedBy);   // no ICurrentUser in test → fallback
    }

    [Fact]
    public async Task Update_UnknownCode_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var result = await sut.Update("NOPE",
            new UpdateTenantDto(Name: "anything", RowVersion: TestRowVersion),
            CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
    }

    [Fact]
    public async Task Update_DoesNotChangeCodeOrStatusOrCreatedAt()
    {
        await using var db = NewDb();
        var original = SeedTenant(db, code: "ACME", name: "Acme Corp");
        var originalCreatedAt = original.CreatedAt;
        var originalStatus    = original.Status;
        var sut = NewController(db);

        await sut.Update("ACME",
            new UpdateTenantDto(Name: "Changed", RowVersion: TestRowVersion),
            CancellationToken.None);

        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Code == "ACME");
        Assert.Equal("ACME", reloaded.Code);                     // code unchanged
        Assert.Equal(originalStatus, reloaded.Status);           // status unchanged
        Assert.Equal(originalCreatedAt, reloaded.CreatedAt);     // createdAt unchanged
    }

    [Fact]
    public async Task Update_ClearsNullableFields_WhenDtoFieldsAreNull()
    {
        await using var db = NewDb();
        var original = SeedTenant(db, code: "ACME", name: "Acme Corp");
        original.DisplayName  = "Acme";
        original.ContactEmail = "ops@acme.example";
        original.Notes        = "some note";
        await db.SaveChangesAsync();
        var sut = NewController(db);

        // PUT semantics: omit == null == clear.
        await sut.Update("ACME",
            new UpdateTenantDto(Name: "Acme Corp", RowVersion: TestRowVersion),
            CancellationToken.None);

        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Code == "ACME");
        Assert.Null(reloaded.DisplayName);
        Assert.Null(reloaded.ContactEmail);
        Assert.Null(reloaded.Notes);
    }

    // ============================================================
    // Deactivate
    // ============================================================

    [Fact]
    public async Task Deactivate_ActiveTenant_SetsInactiveAndDeactivatedAt()
    {
        await using var db = NewDb();
        SeedTenant(db, code: "ACME", status: TenantStatus.Active);
        var sut = NewController(db);

        var result = await sut.Deactivate("ACME", new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Code == "ACME");
        Assert.Equal(TenantStatus.Inactive, reloaded.Status);
        Assert.NotNull(reloaded.DeactivatedAt);
        Assert.NotNull(reloaded.UpdatedAt);
    }

    [Fact]
    public async Task Deactivate_AlreadyInactive_IsIdempotent()
    {
        await using var db = NewDb();
        var tenant = SeedTenant(db, code: "ACME", status: TenantStatus.Inactive);
        tenant.DeactivatedAt = DateTimeOffset.UtcNow.AddDays(-3);
        await db.SaveChangesAsync();
        var priorDeactivatedAt = tenant.DeactivatedAt;
        var sut = NewController(db);

        var result = await sut.Deactivate("ACME", new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Code == "ACME");
        Assert.Equal(TenantStatus.Inactive, reloaded.Status);
        // The earlier DeactivatedAt stamp must not be overwritten on a no-op call.
        Assert.Equal(priorDeactivatedAt, reloaded.DeactivatedAt);
    }

    [Fact]
    public async Task Deactivate_ArchivedTenant_ReturnsConflictProblem()
    {
        await using var db = NewDb();
        SeedTenant(db, code: "ACME", status: TenantStatus.Archived);
        var sut = NewController(db);

        var result = await sut.Deactivate("ACME", new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        AssertProblem(result, 409, "/conflict");
        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Code == "ACME");
        Assert.Equal(TenantStatus.Archived, reloaded.Status);   // unchanged
    }

    [Fact]
    public async Task Deactivate_UnknownCode_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var result = await sut.Deactivate("NOPE", new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
    }

    // ============================================================
    // Reactivate
    // ============================================================

    [Fact]
    public async Task Reactivate_InactiveTenant_SetsActiveAndClearsDeactivatedAt()
    {
        await using var db = NewDb();
        var tenant = SeedTenant(db, code: "ACME", status: TenantStatus.Inactive);
        tenant.DeactivatedAt = DateTimeOffset.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        var sut = NewController(db);

        var result = await sut.Reactivate("ACME", new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Code == "ACME");
        Assert.Equal(TenantStatus.Active, reloaded.Status);
        Assert.Null(reloaded.DeactivatedAt);
        Assert.NotNull(reloaded.UpdatedAt);
    }

    [Fact]
    public async Task Reactivate_AlreadyActive_IsIdempotent()
    {
        await using var db = NewDb();
        SeedTenant(db, code: "ACME", status: TenantStatus.Active);
        var sut = NewController(db);

        var result = await sut.Reactivate("ACME", new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Code == "ACME");
        Assert.Equal(TenantStatus.Active, reloaded.Status);
        Assert.Null(reloaded.DeactivatedAt);
    }

    [Fact]
    public async Task Reactivate_ArchivedTenant_ReturnsConflictProblem()
    {
        await using var db = NewDb();
        SeedTenant(db, code: "ACME", status: TenantStatus.Archived);
        var sut = NewController(db);

        var result = await sut.Reactivate("ACME", new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        AssertProblem(result, 409, "/conflict");
        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Code == "ACME");
        Assert.Equal(TenantStatus.Archived, reloaded.Status);
    }

    [Fact]
    public async Task Reactivate_UnknownCode_ReturnsNotFoundProblem()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var result = await sut.Reactivate("NOPE", new LifecycleTransitionDto(RowVersion: TestRowVersion), CancellationToken.None);

        AssertProblem(result, 404, "/not-found");
    }

    // ============================================================
    // Audit interceptor
    // ============================================================

    [Fact]
    public async Task SaveChanges_StampsCreatedAtAndCreatedBy_OnInsert()
    {
        await using var db = NewDb();

        // New tenant with default CreatedAt (will be overwritten to "now"
        // because the initializer sets it to DateTimeOffset.UtcNow; the
        // interceptor treats a default(DateTimeOffset) value as unset).
        var tenant = new Tenant("ACME", "Acme Corp") { CreatedAt = default };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Code == "ACME");
        Assert.NotEqual(default, reloaded.CreatedAt);
        Assert.True(reloaded.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal("system", reloaded.CreatedBy);   // no ICurrentUser registered
    }

    [Fact]
    public async Task SaveChanges_DoesNotOverwriteCreatedAt_OnUpdate()
    {
        await using var db = NewDb();
        var original = SeedTenant(db, code: "ACME", name: "Acme Corp");
        var originalCreatedAt = original.CreatedAt;

        var tenant = await db.Tenants.FirstAsync(t => t.Code == "ACME");
        tenant.Name = "Renamed";
        await db.SaveChangesAsync();

        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Code == "ACME");
        Assert.Equal(originalCreatedAt, reloaded.CreatedAt);
        Assert.NotNull(reloaded.UpdatedAt);
        Assert.Equal("system", reloaded.UpdatedBy);
    }
}
