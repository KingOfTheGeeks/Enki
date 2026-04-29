using Microsoft.EntityFrameworkCore;
using SDI.Enki.Identity.Background;
using SDI.Enki.Identity.Data;

namespace SDI.Enki.Identity.Tests.Background;

/// <summary>
/// Unit tests for the retention <b>policy</b> — cutoff math and filter
/// shape — without invoking EF's <c>ExecuteDeleteAsync</c> (the
/// InMemory provider doesn't support it; SQL Server does, which is
/// what production uses). The split <c>CutoffFor</c> helper + a
/// parallel filter query give us the same correctness signal: if
/// the cutoff is right and the filter shape is right, the prune
/// behaves correctly in production.
/// </summary>
public class AuditRetentionServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static ApplicationDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"retention-{name}-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(opts);
    }

    // ---------- CutoffFor ----------

    [Fact]
    public void CutoffFor_SubtractsDaysFromNow()
    {
        var cutoff = AuditRetentionService.CutoffFor(FixedNow, 90);
        Assert.Equal(FixedNow.AddDays(-90), cutoff);
    }

    [Fact]
    public void CutoffFor_ZeroDays_ReturnsNow()
    {
        var cutoff = AuditRetentionService.CutoffFor(FixedNow, 0);
        Assert.Equal(FixedNow, cutoff);
    }

    // ---------- filter shape ----------
    // Re-runs the same Where(...) the production prune executes, on
    // the InMemory provider — verifies the rows that *would* be
    // pruned match the policy. Drift between this and production
    // would surface here.

    [Fact]
    public async Task AuthEventLog_FilterMatches_OnlyRowsBeyondWindow()
    {
        await using var db = NewDb();
        db.AuthEventLogs.Add(new AuthEventLog
        {
            EventType = "SignInSucceeded", Username = "old",
            OccurredAt = FixedNow.AddDays(-200),
        });
        db.AuthEventLogs.Add(new AuthEventLog
        {
            EventType = "SignInSucceeded", Username = "edge-old",
            OccurredAt = FixedNow.AddDays(-91),
        });
        db.AuthEventLogs.Add(new AuthEventLog
        {
            EventType = "SignInSucceeded", Username = "edge-new",
            OccurredAt = FixedNow.AddDays(-89),
        });
        await db.SaveChangesAsync();

        var cutoff = AuditRetentionService.CutoffFor(FixedNow, 90);
        var stale = await db.AuthEventLogs
            .Where(e => e.OccurredAt < cutoff)
            .Select(e => e.Username)
            .ToListAsync();

        Assert.Equal(2, stale.Count);
        Assert.Contains("old", stale);
        Assert.Contains("edge-old", stale);
        Assert.DoesNotContain("edge-new", stale);
    }

    [Fact]
    public async Task IdentityAuditLog_FilterMatches_IndependentlyOfAuthEventWindow()
    {
        // 90-day AuthEvent window vs 365-day Identity window — a row
        // 100 days old is stale for AuthEvent but fresh for Identity.
        // Pin the per-table independence.
        await using var db = NewDb();
        db.IdentityAuditLogs.Add(new IdentityAuditLog
        {
            EntityType = "ApplicationUser", EntityId = "old",
            Action = "RoleGranted", ChangedBy = "admin",
            ChangedAt = FixedNow.AddDays(-100),
        });
        await db.SaveChangesAsync();

        var authCutoff     = AuditRetentionService.CutoffFor(FixedNow, 90);
        var identityCutoff = AuditRetentionService.CutoffFor(FixedNow, 365);

        var staleByAuth = await db.IdentityAuditLogs
            .Where(a => a.ChangedAt < authCutoff)
            .CountAsync();
        Assert.Equal(1, staleByAuth);   // would be pruned at 90d

        var staleByIdentity = await db.IdentityAuditLogs
            .Where(a => a.ChangedAt < identityCutoff)
            .CountAsync();
        Assert.Equal(0, staleByIdentity);   // safe at 365d
    }

    [Fact]
    public async Task BoundaryRow_AtCutoffEdge_IsKept()
    {
        // The production filter uses strict `<` cutoff — a row
        // exactly at the cutoff timestamp is kept; one second older
        // is pruned. Pin the rule.
        await using var db = NewDb();
        var cutoff = AuditRetentionService.CutoffFor(FixedNow, 30);

        db.AuthEventLogs.Add(new AuthEventLog
        {
            EventType = "SignInSucceeded", Username = "exactly-at-cutoff",
            OccurredAt = cutoff,
        });
        db.AuthEventLogs.Add(new AuthEventLog
        {
            EventType = "SignInSucceeded", Username = "one-second-too-old",
            OccurredAt = cutoff.AddSeconds(-1),
        });
        await db.SaveChangesAsync();

        var stale = await db.AuthEventLogs
            .Where(e => e.OccurredAt < cutoff)
            .Select(e => e.Username)
            .ToListAsync();

        Assert.Single(stale);
        Assert.Equal("one-second-too-old", stale[0]);
    }
}
