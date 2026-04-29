using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Core.Master.Users;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Audit;
using SDI.Enki.Shared.Paging;
using SDI.Enki.WebApi.Controllers;

namespace SDI.Enki.WebApi.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="MasterAuditController"/>.
/// Pins the read shape — paging, filters (date range, entityType,
/// action, changedBy), per-entity sub-route, and the CSV export
/// content shape — against an InMemory <c>EnkiMasterDbContext</c>
/// whose <c>SaveChangesAsync</c> override populates MasterAuditLog
/// rows automatically. Auth-policy gating is validated upstream;
/// these tests bypass the policy filter.
/// </summary>
public class MasterAuditControllerTests
{
    private static EnkiMasterDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseInMemoryDatabase($"master-audit-{name}-{Guid.NewGuid():N}")
            .Options;
        return new EnkiMasterDbContext(opts);
    }

    private static MasterAuditController NewController(EnkiMasterDbContext db) =>
        new(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

    /// <summary>
    /// Seed three Tenants so the master audit log carries three Created
    /// rows — enough material to exercise paging + ordering. Each
    /// Tenant.Add → one MasterAuditLog row, populated by the audit
    /// interceptor on SaveChangesAsync.
    /// </summary>
    private static async Task<EnkiMasterDbContext> SeedThreeTenantsAsync()
    {
        var db = NewDb();
        db.Tenants.Add(new Tenant("ACME",  "ACME Corp")    { Status = TenantStatus.Active });
        await db.SaveChangesAsync();
        db.Tenants.Add(new Tenant("BAKKEN", "Bakken Oil")  { Status = TenantStatus.Active });
        await db.SaveChangesAsync();
        db.Tenants.Add(new Tenant("CABOT",  "Cabot Lease") { Status = TenantStatus.Active });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task List_ReturnsPagedRowsNewestFirst()
    {
        await using var db = await SeedThreeTenantsAsync();
        var sut = NewController(db);

        var page = await sut.List(ct: default);

        Assert.Equal(3, page.Total);
        Assert.Equal(3, page.Items.Count);
        Assert.All(page.Items, r => Assert.Equal("Tenant", r.EntityType));
        Assert.All(page.Items, r => Assert.Equal("Created", r.Action));

        // Newest first: descending ChangedAt.
        for (var i = 1; i < page.Items.Count; i++)
            Assert.True(page.Items[i - 1].ChangedAt >= page.Items[i].ChangedAt);
    }

    [Fact]
    public async Task List_HonoursSkipAndTake()
    {
        await using var db = await SeedThreeTenantsAsync();
        var sut = NewController(db);

        var first  = await sut.List(skip: 0, take: 2, ct: default);
        Assert.Equal(3, first.Total);
        Assert.Equal(2, first.Items.Count);
        Assert.True(first.HasMore);

        var second = await sut.List(skip: 2, take: 2, ct: default);
        Assert.Equal(3, second.Total);
        Assert.Single(second.Items);
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task List_ClampsTakeToMaxCeiling()
    {
        await using var db = await SeedThreeTenantsAsync();
        var sut = NewController(db);

        var page = await sut.List(skip: 0, take: 10000, ct: default);
        Assert.Equal(500, page.Take);
    }

    [Fact]
    public async Task List_FromTo_FiltersByChangedAtRange()
    {
        await using var db = await SeedThreeTenantsAsync();

        // All three rows landed within the last second; bracket the
        // range so far-past + far-future filters drop everything.
        var sut = NewController(db);

        var future = await sut.List(
            from: DateTimeOffset.UtcNow.AddDays(1),
            ct: default);
        Assert.Equal(0, future.Total);

        var ancient = await sut.List(
            to: DateTimeOffset.UtcNow.AddDays(-1),
            ct: default);
        Assert.Equal(0, ancient.Total);

        var window = await sut.List(
            from: DateTimeOffset.UtcNow.AddMinutes(-5),
            to:   DateTimeOffset.UtcNow.AddMinutes(5),
            ct: default);
        Assert.Equal(3, window.Total);
    }

    [Fact]
    public async Task List_EntityTypeFilter_FiltersExactly()
    {
        // Mix Tenant + License rows so the filter has something to discriminate.
        await using var db = NewDb();
        db.Tenants.Add(new Tenant("ACME", "ACME Corp") { Status = TenantStatus.Active });
        await db.SaveChangesAsync();
        db.Users.Add(new User("alice", Guid.NewGuid()));
        await db.SaveChangesAsync();   // User is NOT IAuditable — won't add a row

        // Drop in a synthetic AuthzDenial row directly so we don't have
        // to go through the auth pipeline to seed one.
        db.MasterAuditLogs.Add(new SDI.Enki.Core.Master.Audit.MasterAuditLog
        {
            EntityType = "AuthzDenial",
            EntityId   = "(global)",
            Action     = "Denied",
            ChangedAt  = DateTimeOffset.UtcNow,
            ChangedBy  = "test-actor",
        });
        await db.SaveChangesAsync();

        var sut = NewController(db);

        var denials = await sut.List(entityType: "AuthzDenial", ct: default);
        Assert.Equal(1, denials.Total);
        Assert.All(denials.Items, r => Assert.Equal("AuthzDenial", r.EntityType));

        var tenants = await sut.List(entityType: "Tenant", ct: default);
        Assert.Equal(1, tenants.Total);
        Assert.All(tenants.Items, r => Assert.Equal("Tenant", r.EntityType));
    }

    [Fact]
    public async Task List_ActionFilter_FiltersExactly()
    {
        await using var db = NewDb();
        var t = new Tenant("ACME", "ACME Corp") { Status = TenantStatus.Active };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();

        // Modify so we get a Created + Updated pair.
        t.Name = "ACME Renamed";
        await db.SaveChangesAsync();

        var sut = NewController(db);

        var creates = await sut.List(action: "Created", ct: default);
        Assert.Equal(1, creates.Total);

        var updates = await sut.List(action: "Updated", ct: default);
        Assert.Equal(1, updates.Total);

        var deletes = await sut.List(action: "Deleted", ct: default);
        Assert.Equal(0, deletes.Total);
    }

    [Fact]
    public async Task List_ChangedByFilter_DoesPartialMatch()
    {
        // The audit interceptor stamps ChangedBy from ICurrentUser,
        // which falls back to "system" when the context has none.
        await using var db = await SeedThreeTenantsAsync();
        var sut = NewController(db);

        var systemRows = await sut.List(changedBy: "syst", ct: default);
        Assert.Equal(3, systemRows.Total);

        var noMatch = await sut.List(changedBy: "definitely-not-an-actor", ct: default);
        Assert.Equal(0, noMatch.Total);
    }

    [Fact]
    public async Task ListForEntity_ScopesToOneEntityRow()
    {
        await using var db = NewDb();
        var t1 = new Tenant("ACME",   "ACME Corp")   { Status = TenantStatus.Active };
        var t2 = new Tenant("BAKKEN", "Bakken Oil") { Status = TenantStatus.Active };
        db.Tenants.AddRange(t1, t2);
        await db.SaveChangesAsync();

        // ACME gets one update, Bakken doesn't.
        t1.Name = "ACME Renamed";
        await db.SaveChangesAsync();

        var sut = NewController(db);

        var acmeHistory = await sut.ListForEntity("Tenant", t1.Id.ToString(), skip: null, take: null, ct: default);
        Assert.Equal(2, acmeHistory.Total);    // Created + Updated
        Assert.Contains(acmeHistory.Items, r => r.Action == "Updated");

        var bakkenHistory = await sut.ListForEntity("Tenant", t2.Id.ToString(), skip: null, take: null, ct: default);
        Assert.Single(bakkenHistory.Items);    // Created only
    }

    [Fact]
    public async Task ExportCsv_ProducesCsvDownload()
    {
        await using var db = await SeedThreeTenantsAsync();
        var sut = NewController(db);

        var result = await sut.ExportCsv(ct: default);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        Assert.StartsWith("enki-master-audit-", file.FileDownloadName);
        Assert.EndsWith(".csv", file.FileDownloadName);

        var body = System.Text.Encoding.UTF8.GetString(file.FileContents!);
        Assert.StartsWith("Id,ChangedAt,EntityType,EntityId,Action,ChangedBy", body);
        // Three data rows + header + trailing newline → at least 4 lines.
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
    }
}
