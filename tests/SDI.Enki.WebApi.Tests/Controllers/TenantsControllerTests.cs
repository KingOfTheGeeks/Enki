using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning;
using SDI.Enki.Infrastructure.Provisioning.Models;
using SDI.Enki.Shared.Tenants;
using SDI.Enki.WebApi.Controllers;
using SDI.Enki.WebApi.Tests.Fakes;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Direct controller tests against an in-memory <see cref="AthenaMasterDbContext"/>.
/// These exercise the controller's action logic and EF query shapes without
/// spinning up the full HTTP pipeline, which would require OpenIddict +
/// auth test harnesses. Full HTTP integration is left for a separate pass.
///
/// Each test gets a fresh database (unique InMemory name) so parallel xunit
/// execution doesn't cross-pollute state.
/// </summary>
public class TenantsControllerTests
{
    // ---------- fixture helpers ----------

    private static AthenaMasterDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<AthenaMasterDbContext>()
            .UseInMemoryDatabase($"tenants-{name}-{Guid.NewGuid():N}")
            .Options;
        return new AthenaMasterDbContext(opts);
    }

    private static TenantsController NewController(
        AthenaMasterDbContext db,
        ITenantProvisioningService? provisioning = null)
        => new(db, provisioning ?? new FakeTenantProvisioningService());

    private static Tenant SeedTenant(
        AthenaMasterDbContext db,
        string code = "ACME",
        string name = "Acme Corp",
        TenantStatus? status = null,
        TenantDatabaseKind? database = null)
    {
        var tenant = new Tenant(code, name)
        {
            Status = status ?? TenantStatus.Active,
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
    public async Task Get_UnknownCode_ReturnsNotFound()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var result = await sut.Get("NOPE", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Get_TenantWithoutActiveDatabase_DoesNotThrow()
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
        Assert.Equal("NEWCO", created.RouteValues?["code"]);
        var body = Assert.IsType<ProvisionTenantResult>(created.Value);
        Assert.Equal("NEWCO", body.Code);
    }

    [Fact]
    public async Task Provision_ServiceThrows_ReturnsBadRequest()
    {
        await using var db = NewDb();
        var partialId = Guid.NewGuid();
        var fake = new FakeTenantProvisioningService
        {
            ThrowOnProvision = new TenantProvisioningException(
                "database already exists", partialId),
        };
        var sut = NewController(db, fake);

        var result = await sut.Provision(
            new ProvisionTenantDto("DUPE", "Dupe Co"),
            CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        // Payload is an anonymous object with { error, partialTenantId }.
        Assert.NotNull(bad.Value);
        var json = System.Text.Json.JsonSerializer.Serialize(bad.Value);
        Assert.Contains("database already exists", json);
        Assert.Contains(partialId.ToString(), json);
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
            Notes: "updated");

        var result = await sut.Update("ACME", dto, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Code == "ACME");
        Assert.Equal("New Name", reloaded.Name);
        Assert.Equal("New Display", reloaded.DisplayName);
        Assert.Equal("ops@acme.example", reloaded.ContactEmail);
        Assert.Equal("updated", reloaded.Notes);
        Assert.NotNull(reloaded.UpdatedAt);
        Assert.True(reloaded.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Update_UnknownCode_ReturnsNotFound()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var result = await sut.Update("NOPE",
            new UpdateTenantDto(Name: "anything"),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
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
            new UpdateTenantDto(Name: "Changed"),
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
            new UpdateTenantDto(Name: "Acme Corp"),
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

        var result = await sut.Deactivate("ACME", CancellationToken.None);

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
        tenant.UpdatedAt     = DateTimeOffset.UtcNow.AddDays(-3);
        await db.SaveChangesAsync();
        var priorDeactivatedAt = tenant.DeactivatedAt;
        var sut = NewController(db);

        var result = await sut.Deactivate("ACME", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Code == "ACME");
        Assert.Equal(TenantStatus.Inactive, reloaded.Status);
        // The earlier DeactivatedAt stamp must not be overwritten on a no-op call.
        Assert.Equal(priorDeactivatedAt, reloaded.DeactivatedAt);
    }

    [Fact]
    public async Task Deactivate_ArchivedTenant_ReturnsConflict()
    {
        await using var db = NewDb();
        SeedTenant(db, code: "ACME", status: TenantStatus.Archived);
        var sut = NewController(db);

        var result = await sut.Deactivate("ACME", CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Code == "ACME");
        Assert.Equal(TenantStatus.Archived, reloaded.Status);   // unchanged
    }

    [Fact]
    public async Task Deactivate_UnknownCode_ReturnsNotFound()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var result = await sut.Deactivate("NOPE", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
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

        var result = await sut.Reactivate("ACME", CancellationToken.None);

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

        var result = await sut.Reactivate("ACME", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Code == "ACME");
        Assert.Equal(TenantStatus.Active, reloaded.Status);
        Assert.Null(reloaded.DeactivatedAt);
    }

    [Fact]
    public async Task Reactivate_ArchivedTenant_ReturnsConflict()
    {
        await using var db = NewDb();
        SeedTenant(db, code: "ACME", status: TenantStatus.Archived);
        var sut = NewController(db);

        var result = await sut.Reactivate("ACME", CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        var reloaded = await db.Tenants.AsNoTracking().FirstAsync(t => t.Code == "ACME");
        Assert.Equal(TenantStatus.Archived, reloaded.Status);
    }

    [Fact]
    public async Task Reactivate_UnknownCode_ReturnsNotFound()
    {
        await using var db = NewDb();
        var sut = NewController(db);

        var result = await sut.Reactivate("NOPE", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }
}
