using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Identity.Controllers;
using SDI.Enki.Identity.Data;

namespace SDI.Enki.Identity.Tests.Controllers;

/// <summary>
/// Direct controller tests for <see cref="AuthEventsController"/>.
/// Pins the read shape — paging, the username/eventType/from/to filters,
/// and CSV export — against an InMemory <c>ApplicationDbContext</c>
/// with synthetic <see cref="AuthEventLog"/> rows. The real write path
/// (Login/Logout/Exchange call sites + AuthEventLogger) is exercised
/// elsewhere; these tests cover the read surface end-to-end.
/// </summary>
public class AuthEventsControllerTests
{
    private static ApplicationDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"auth-events-{name}-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(opts);
    }

    /// <summary>
    /// Anchor for seeded timestamps. Tests pin from/to bounds against
    /// this rather than capturing a fresh <c>UtcNow</c> at query time —
    /// a clock-drift race between seed-time and query-time would
    /// otherwise flake the boundary-based filter tests.
    /// </summary>
    private static readonly DateTimeOffset SeedNow = DateTimeOffset.UtcNow;

    /// <summary>
    /// Mix of event types + users + timestamps so filter tests have
    /// material to discriminate on. Five rows total, spaced at hour
    /// intervals so floating-point boundary checks don't matter.
    /// </summary>
    private static async Task<ApplicationDbContext> SeedMixedFeedAsync()
    {
        var db = NewDb();

        db.AuthEventLogs.Add(new AuthEventLog
        {
            EventType = "SignInSucceeded", Username = "alice",
            IdentityId = "alice-id", IpAddress = "10.0.0.1",
            OccurredAt = SeedNow.AddHours(-1),
        });
        db.AuthEventLogs.Add(new AuthEventLog
        {
            EventType = "SignInFailed", Username = "alice",
            IdentityId = "alice-id", IpAddress = "10.0.0.1",
            Detail = """{"reason":"BadPassword"}""",
            OccurredAt = SeedNow.AddHours(-2),
        });
        db.AuthEventLogs.Add(new AuthEventLog
        {
            EventType = "TokenIssued", Username = "alice",
            IdentityId = "alice-id", IpAddress = "10.0.0.1",
            Detail = """{"grantType":"authorization_code"}""",
            OccurredAt = SeedNow.AddHours(-3),
        });
        db.AuthEventLogs.Add(new AuthEventLog
        {
            EventType = "SignInFailed", Username = "bob",
            IdentityId = "bob-id", IpAddress = "10.0.0.2",
            Detail = """{"reason":"BadPassword"}""",
            OccurredAt = SeedNow.AddHours(-4),
        });
        db.AuthEventLogs.Add(new AuthEventLog
        {
            EventType = "SignOut", Username = "alice",
            IdentityId = "alice-id", IpAddress = "10.0.0.1",
            OccurredAt = SeedNow.AddHours(-5),
        });

        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task List_ReturnsPagedRowsNewestFirst()
    {
        await using var db = await SeedMixedFeedAsync();
        var sut = new AuthEventsController(db);

        var page = await sut.List(ct: default);

        Assert.Equal(5, page.Total);
        Assert.Equal(5, page.Items.Count);
        for (var i = 1; i < page.Items.Count; i++)
            Assert.True(page.Items[i - 1].OccurredAt >= page.Items[i].OccurredAt);
    }

    [Fact]
    public async Task List_HonoursSkipAndTake()
    {
        await using var db = await SeedMixedFeedAsync();
        var sut = new AuthEventsController(db);

        var page = await sut.List(skip: 2, take: 2, ct: default);
        Assert.Equal(5, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task List_UsernameFilter_FiltersExactly()
    {
        await using var db = await SeedMixedFeedAsync();
        var sut = new AuthEventsController(db);

        var aliceEvents = await sut.List(username: "alice", ct: default);
        Assert.Equal(4, aliceEvents.Total);
        Assert.All(aliceEvents.Items, r => Assert.Equal("alice", r.Username));

        var bobEvents = await sut.List(username: "bob", ct: default);
        Assert.Equal(1, bobEvents.Total);
    }

    [Fact]
    public async Task List_EventTypeFilter_FiltersExactly()
    {
        await using var db = await SeedMixedFeedAsync();
        var sut = new AuthEventsController(db);

        var failures = await sut.List(eventType: "SignInFailed", ct: default);
        Assert.Equal(2, failures.Total);
        Assert.All(failures.Items, r => Assert.Equal("SignInFailed", r.EventType));
    }

    [Fact]
    public async Task List_UsernameAndEventType_Combined()
    {
        await using var db = await SeedMixedFeedAsync();
        var sut = new AuthEventsController(db);

        var aliceFailures = await sut.List(
            username: "alice", eventType: "SignInFailed", ct: default);
        Assert.Equal(1, aliceFailures.Total);
    }

    [Fact]
    public async Task List_FromTo_FiltersByOccurredAtRange()
    {
        await using var db = await SeedMixedFeedAsync();
        var sut = new AuthEventsController(db);

        var future = await sut.List(from: SeedNow.AddDays(1), ct: default);
        Assert.Equal(0, future.Total);

        // 3 rows are within the last 3.5h of SeedNow (offsets -1, -2, -3).
        var window = await sut.List(from: SeedNow.AddHours(-3.5), ct: default);
        Assert.Equal(3, window.Total);
    }

    [Fact]
    public async Task ExportCsv_ProducesCsvDownload()
    {
        await using var db = await SeedMixedFeedAsync();
        var sut = new AuthEventsController(db);

        var result = await sut.ExportCsv(ct: default);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        Assert.StartsWith("enki-auth-events-", file.FileDownloadName);
        Assert.EndsWith(".csv", file.FileDownloadName);

        var body = System.Text.Encoding.UTF8.GetString(file.FileContents!);
        Assert.StartsWith("Id,OccurredAt,EventType,Username,IdentityId,IpAddress,UserAgent,Detail", body);
        // Five data rows + header → at least 6 lines.
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(6, lines.Length);
    }

    [Fact]
    public async Task ExportCsv_PassesFiltersThrough()
    {
        await using var db = await SeedMixedFeedAsync();
        var sut = new AuthEventsController(db);

        var result = await sut.ExportCsv(eventType: "SignInFailed", ct: default);
        var file = Assert.IsType<FileContentResult>(result);

        var body = System.Text.Encoding.UTF8.GetString(file.FileContents!);
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // Header + 2 SignInFailed rows.
        Assert.Equal(3, lines.Length);
    }
}
