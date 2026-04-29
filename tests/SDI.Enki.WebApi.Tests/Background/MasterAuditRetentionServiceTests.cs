using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Audit;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.WebApi.Background;

namespace SDI.Enki.WebApi.Tests.Background;

/// <summary>
/// Unit tests for the master retention <b>policy</b> — cutoff math
/// and filter shape — without invoking EF's <c>ExecuteDeleteAsync</c>
/// (the InMemory provider doesn't support it; SQL Server does, which
/// is what production uses). The split <c>CutoffFor</c> helper + a
/// parallel filter query give us the same correctness signal.
/// </summary>
public class MasterAuditRetentionServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static EnkiMasterDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var opts = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseInMemoryDatabase($"master-retention-{name}-{Guid.NewGuid():N}")
            .Options;
        return new EnkiMasterDbContext(opts);
    }

    [Fact]
    public void CutoffFor_SubtractsDaysFromNow()
    {
        var cutoff = MasterAuditRetentionService.CutoffFor(FixedNow, 365);
        Assert.Equal(FixedNow.AddDays(-365), cutoff);
    }

    [Fact]
    public async Task FilterMatches_OnlyRowsBeyondWindow()
    {
        await using var db = NewDb();
        db.MasterAuditLogs.Add(new MasterAuditLog
        {
            EntityType = "Tenant", EntityId = "ancient",
            Action = "Created", ChangedBy = "system",
            ChangedAt = FixedNow.AddDays(-400),
        });
        db.MasterAuditLogs.Add(new MasterAuditLog
        {
            EntityType = "Tenant", EntityId = "fresh",
            Action = "Created", ChangedBy = "system",
            ChangedAt = FixedNow.AddDays(-100),
        });
        await db.SaveChangesAsync();

        var cutoff = MasterAuditRetentionService.CutoffFor(FixedNow, 365);
        var stale = await db.MasterAuditLogs
            .Where(a => a.ChangedAt < cutoff)
            .Select(a => a.EntityId)
            .ToListAsync();

        Assert.Single(stale);
        Assert.Equal("ancient", stale[0]);
    }

    [Fact]
    public async Task BoundaryRow_AtCutoffEdge_IsKept()
    {
        // Strict `<` cutoff — row exactly at cutoff is kept; one
        // second older is pruned.
        await using var db = NewDb();
        var cutoff = MasterAuditRetentionService.CutoffFor(FixedNow, 30);

        db.MasterAuditLogs.Add(new MasterAuditLog
        {
            EntityType = "Tenant", EntityId = "exactly-at-cutoff",
            Action = "Created", ChangedBy = "system",
            ChangedAt = cutoff,
        });
        db.MasterAuditLogs.Add(new MasterAuditLog
        {
            EntityType = "Tenant", EntityId = "one-second-too-old",
            Action = "Created", ChangedBy = "system",
            ChangedAt = cutoff.AddSeconds(-1),
        });
        await db.SaveChangesAsync();

        var stale = await db.MasterAuditLogs
            .Where(a => a.ChangedAt < cutoff)
            .Select(a => a.EntityId)
            .ToListAsync();

        Assert.Single(stale);
        Assert.Equal("one-second-too-old", stale[0]);
    }
}
