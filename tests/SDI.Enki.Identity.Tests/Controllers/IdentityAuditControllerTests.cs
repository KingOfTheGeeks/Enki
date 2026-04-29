using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Identity.Controllers;
using SDI.Enki.Identity.Data;

namespace SDI.Enki.Identity.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="IdentityAuditController"/>.
/// Pins the read shape — paging, filters (date range, action, changedBy),
/// per-entity sub-route, and CSV export — against an InMemory
/// <c>ApplicationDbContext</c>. Auth-policy gating is validated upstream;
/// these tests bypass the policy filter.
/// </summary>
public class IdentityAuditControllerTests
{
    private static ApplicationDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"identity-audit-{name}-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(opts);
    }

    /// <summary>
    /// Seed three IdentityAuditLog rows directly. The real write path
    /// is exercised by the AdminUsersController tests; here we just need
    /// material to read.
    /// </summary>
    private static async Task<ApplicationDbContext> SeedThreeRowsAsync()
    {
        var db = NewDb();
        for (var i = 0; i < 3; i++)
        {
            db.IdentityAuditLogs.Add(new IdentityAuditLog
            {
                EntityType = "ApplicationUser",
                EntityId   = $"user-{i}",
                Action     = i == 0 ? "RoleGranted" : "PasswordReset",
                ChangedAt  = DateTimeOffset.UtcNow.AddMinutes(-i),
                ChangedBy  = "admin-actor",
            });
            await db.SaveChangesAsync();
        }
        return db;
    }

    [Fact]
    public async Task List_ReturnsPagedRowsNewestFirst()
    {
        await using var db = await SeedThreeRowsAsync();
        var sut = new IdentityAuditController(db);

        var page = await sut.List(ct: default);

        Assert.Equal(3, page.Total);
        Assert.Equal(3, page.Items.Count);
        for (var i = 1; i < page.Items.Count; i++)
            Assert.True(page.Items[i - 1].ChangedAt >= page.Items[i].ChangedAt);
    }

    [Fact]
    public async Task List_HonoursSkipAndTake()
    {
        await using var db = await SeedThreeRowsAsync();
        var sut = new IdentityAuditController(db);

        var first = await sut.List(skip: 0, take: 2, ct: default);
        Assert.Equal(3, first.Total);
        Assert.Equal(2, first.Items.Count);
        Assert.True(first.HasMore);

        var second = await sut.List(skip: 2, take: 2, ct: default);
        Assert.Single(second.Items);
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task List_ActionFilter_FiltersExactly()
    {
        await using var db = await SeedThreeRowsAsync();
        var sut = new IdentityAuditController(db);

        var grants = await sut.List(action: "RoleGranted", ct: default);
        Assert.Equal(1, grants.Total);
        Assert.All(grants.Items, r => Assert.Equal("RoleGranted", r.Action));

        var resets = await sut.List(action: "PasswordReset", ct: default);
        Assert.Equal(2, resets.Total);
    }

    [Fact]
    public async Task List_ChangedByFilter_DoesPartialMatch()
    {
        await using var db = await SeedThreeRowsAsync();
        var sut = new IdentityAuditController(db);

        var matches = await sut.List(changedBy: "admin", ct: default);
        Assert.Equal(3, matches.Total);

        var nope = await sut.List(changedBy: "nobody-by-this-name", ct: default);
        Assert.Equal(0, nope.Total);
    }

    [Fact]
    public async Task List_FromTo_FiltersByChangedAtRange()
    {
        await using var db = await SeedThreeRowsAsync();
        var sut = new IdentityAuditController(db);

        var future = await sut.List(from: DateTimeOffset.UtcNow.AddDays(1), ct: default);
        Assert.Equal(0, future.Total);

        var window = await sut.List(
            from: DateTimeOffset.UtcNow.AddMinutes(-5),
            to:   DateTimeOffset.UtcNow.AddMinutes(5),
            ct: default);
        Assert.Equal(3, window.Total);
    }

    [Fact]
    public async Task ListForEntity_ScopesToOneEntityRow()
    {
        await using var db = NewDb();
        db.IdentityAuditLogs.Add(new IdentityAuditLog
        {
            EntityType = "ApplicationUser",
            EntityId   = "alice-id",
            Action     = "RoleGranted",
            ChangedAt  = DateTimeOffset.UtcNow,
            ChangedBy  = "admin",
        });
        db.IdentityAuditLogs.Add(new IdentityAuditLog
        {
            EntityType = "ApplicationUser",
            EntityId   = "bob-id",
            Action     = "PasswordReset",
            ChangedAt  = DateTimeOffset.UtcNow,
            ChangedBy  = "admin",
        });
        await db.SaveChangesAsync();

        var sut = new IdentityAuditController(db);

        var aliceHistory = await sut.ListForEntity("ApplicationUser", "alice-id",
            skip: null, take: null, ct: default);
        Assert.Single(aliceHistory.Items);
        Assert.Equal("alice-id", aliceHistory.Items[0].EntityId);
    }

    [Fact]
    public async Task ExportCsv_ProducesCsvDownload()
    {
        await using var db = await SeedThreeRowsAsync();
        var sut = new IdentityAuditController(db);

        var result = await sut.ExportCsv(ct: default);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        Assert.StartsWith("enki-identity-audit-", file.FileDownloadName);
        Assert.EndsWith(".csv", file.FileDownloadName);

        var body = System.Text.Encoding.UTF8.GetString(file.FileContents!);
        Assert.StartsWith("Id,ChangedAt,EntityType,EntityId,Action,ChangedBy", body);
    }
}
