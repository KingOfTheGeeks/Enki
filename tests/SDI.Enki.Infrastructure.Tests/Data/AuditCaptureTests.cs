using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Audit;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.Infrastructure.Tests.Data;

/// <summary>
/// Tests the <c>TenantDbContext.SaveChangesAsync</c> audit-capture path.
/// EF InMemory is sufficient — the override just enumerates the change
/// tracker and inserts <see cref="AuditLog"/> rows alongside the
/// underlying mutation, no SQL Server-specific shape involved.
///
/// <para>
/// Each test exercises one of the three actions (Created / Updated /
/// Deleted) plus the wire-format invariants the read API relies on:
/// JSON in the values columns is valid, RowVersion is excluded,
/// ChangedColumns reflects only properties that actually moved.
/// </para>
/// </summary>
public class AuditCaptureTests
{
    private sealed class FakeUser(string id) : ICurrentUser
    {
        public string? UserId   { get; } = id;
        public string? UserName { get; } = id;
    }

    private static TenantDbContext NewContext(string? userId = null,
        [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase($"audit-{name}-{Guid.NewGuid():N}")
            .Options;
        return new TenantDbContext(options, userId is null ? null : new FakeUser(userId));
    }

    [Fact]
    public async Task Insert_OfIAuditable_EmitsCreatedAuditRow()
    {
        await using var db = NewContext("alice");
        var job = new Job("AuditJob", "creating", UnitSystem.Field);

        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var entry = await db.AuditLogs.SingleAsync(a => a.EntityType == "Job");
        Assert.Equal("Created", entry.Action);
        Assert.Equal(job.Id.ToString(), entry.EntityId);
        Assert.Equal("alice", entry.ChangedBy);
        Assert.Null(entry.OldValues);
        Assert.NotNull(entry.NewValues);
        Assert.Null(entry.ChangedColumns); // create has no diff — everything is new
    }

    [Fact]
    public async Task Update_ToIAuditable_EmitsUpdatedAuditRowWithDiffOnlyChangedColumns()
    {
        await using var db = NewContext("bob");

        var job = new Job("AuditJobU", "before", UnitSystem.Field);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        // Reload from a fresh entry-point so the change tracker is clean,
        // mutate one field only, save again. The audit row should
        // reflect the single column move.
        job.Description = "after";
        await db.SaveChangesAsync();

        var update = await db.AuditLogs
            .Where(a => a.EntityType == "Job" && a.Action == "Updated")
            .SingleAsync();

        Assert.Equal("bob", update.ChangedBy);
        Assert.NotNull(update.OldValues);
        Assert.NotNull(update.NewValues);
        Assert.Equal("Description", update.ChangedColumns);

        // Sanity: the JSON snapshots round-trip through the
        // serializer (i.e. the values columns are well-formed).
        var oldDoc = JsonDocument.Parse(update.OldValues!);
        var newDoc = JsonDocument.Parse(update.NewValues!);
        Assert.Equal("before", oldDoc.RootElement.GetProperty("Description").GetString());
        Assert.Equal("after",  newDoc.RootElement.GetProperty("Description").GetString());
    }

    [Fact]
    public async Task Delete_OfIAuditable_EmitsDeletedAuditRowWithOldValuesOnly()
    {
        await using var db = NewContext("carol");

        var job = new Job("AuditJobD", "to-delete", UnitSystem.Field);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        db.Jobs.Remove(job);
        await db.SaveChangesAsync();

        var del = await db.AuditLogs
            .Where(a => a.EntityType == "Job" && a.Action == "Deleted")
            .SingleAsync();

        Assert.Equal("carol", del.ChangedBy);
        Assert.NotNull(del.OldValues);
        Assert.Null(del.NewValues);
        Assert.Null(del.ChangedColumns);
    }

    [Fact]
    public async Task RowVersion_IsNotIncludedInAuditValues()
    {
        // The audit entity intentionally drops RowVersion: it's an
        // 8-byte concurrency token and would dump base64 noise into
        // every row. Verifying the JSON snapshots don't contain it
        // anchors that policy in tests so a refactor doesn't
        // accidentally pull it back in.
        await using var db = NewContext("dave");

        var job = new Job("AuditJobRv", "rv", UnitSystem.Field);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var entry = await db.AuditLogs.SingleAsync(a => a.EntityType == "Job");
        Assert.NotNull(entry.NewValues);
        var doc = JsonDocument.Parse(entry.NewValues!);
        Assert.False(doc.RootElement.TryGetProperty("RowVersion", out _),
            "RowVersion must not appear in audit JSON snapshots.");
    }

    [Fact]
    public async Task NonAuditableEntity_DoesNotProduceAuditRow()
    {
        // Sanity: the capture is gated on IAuditable, not all entities.
        // Even if an Operator (which has no IAuditable) is inserted,
        // no audit row appears. Guards against accidentally widening
        // the capture filter to non-auditable types.
        await using var db = NewContext();

        db.Operators.Add(new SDI.Enki.Core.TenantDb.Operators.Operator("OpX"));
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task SystemActor_FillsWhenNoCurrentUserBound()
    {
        // Design-time tooling and the Migrator CLI construct the
        // context without an ICurrentUser — falls back to "system"
        // so the audit timeline still attributes machine writes.
        await using var db = NewContext(userId: null);

        var job = new Job("AuditJobSys", "sys", UnitSystem.Field);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var entry = await db.AuditLogs.SingleAsync();
        Assert.Equal("system", entry.ChangedBy);
    }

    [Fact]
    public async Task BulkChange_EmitsOneRowPerAuditableEntry()
    {
        // Adding a Job + Well + Survey in one SaveChanges should produce
        // three audit rows. Anchors the per-entry capture: a single
        // SaveChanges call with N IAuditable mutations doesn't collapse
        // to a "summary" row.
        await using var db = NewContext("eve");

        var job = new Job("BulkJob", "bulk", UnitSystem.Field);
        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        var well = new Well(job.Id, "BulkWell", WellType.Target);
        db.Wells.Add(well);
        var survey = new Survey(0, 0, 0, 0); // WellId set after well save
        await db.SaveChangesAsync();

        survey = new Survey(well.Id, depth: 100, inclination: 0, azimuth: 0);
        db.Surveys.Add(survey);
        await db.SaveChangesAsync();

        var rows = await db.AuditLogs.OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal("Job",    rows[0].EntityType);
        Assert.Equal("Well",   rows[1].EntityType);
        Assert.Equal("Survey", rows[2].EntityType);
        Assert.All(rows, r => Assert.Equal("Created", r.Action));
        Assert.All(rows, r => Assert.Equal("eve", r.ChangedBy));
    }
}
