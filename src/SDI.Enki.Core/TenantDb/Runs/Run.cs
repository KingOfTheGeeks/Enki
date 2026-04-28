using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Logs;
using SDI.Enki.Core.TenantDb.Models;
using SDI.Enki.Core.TenantDb.Operators;
using SDI.Enki.Core.TenantDb.Runs.Enums;
using SDI.Enki.Core.TenantDb.Shots;

namespace SDI.Enki.Core.TenantDb.Runs;

/// <summary>
/// A survey run — Gradient, Rotary, or Passive. Single unified record type
/// with a <see cref="Type"/> discriminator. Gradient-specific fields
/// (<see cref="BridleLength"/>, <see cref="CurrentInjection"/>) are nullable;
/// populated only when <c>Type == RunType.Gradient</c>. Rotary and Passive
/// runs have no unique columns beyond the common shape.
///
/// Implements <see cref="IAuditable"/> — CreatedAt / CreatedBy / UpdatedAt /
/// UpdatedBy / RowVersion are managed by
/// <c>TenantDbContext.SaveChangesAsync</c>; don't set them from business code.
/// </summary>
public class Run(string name, string description, double startDepth, double endDepth, RunType type) : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = name;
    public string Description { get; set; } = description;

    public double StartDepth { get; set; } = startDepth;
    public double EndDepth { get; set; } = endDepth;

    public DateTimeOffset? StartTimestamp { get; set; }
    public DateTimeOffset? EndTimestamp { get; set; }

    public RunStatus Status { get; set; } = RunStatus.Planned;
    public RunType Type { get; set; } = type;

    public Guid JobId { get; set; }

    // Gradient-specific — nullable because Rotary and Passive runs don't set them.
    public double? BridleLength { get; set; }
    public double? CurrentInjection { get; set; }

    // IAuditable — managed by TenantDbContext.SaveChangesAsync; treat as read-only.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }

    /// <summary>
    /// Soft-delete marker. <c>null</c> means the run is active and
    /// appears in normal queries; non-null means archived — the row
    /// stays in the DB for audit / restore but is hidden by the
    /// global query filter on <c>TenantDbContext</c>. Set by
    /// <c>RunsController.Delete</c>; cleared by
    /// <c>RunsController.Restore</c>. Same shape as
    /// <see cref="SDI.Enki.Core.TenantDb.Wells.Well.ArchivedAt"/>.
    /// </summary>
    public DateTimeOffset?  ArchivedAt { get; set; }

    // EF navs
    public Job? Job { get; set; }
    public ICollection<Operator> Operators { get; set; } = new List<Operator>();
    public ICollection<Gradient> Gradients { get; set; } = new List<Gradient>();
    public ICollection<Rotary> Rotaries { get; set; } = new List<Rotary>();
    public ICollection<Passive> Passives { get; set; } = new List<Passive>();
    public ICollection<GradientModel> GradientModels { get; set; } = new List<GradientModel>();
    public ICollection<RotaryModel> RotaryModels { get; set; } = new List<RotaryModel>();

    /// <summary>
    /// Logs collected during this run. Single FK from <see cref="Log"/>
    /// (was three nullable run-FKs in the legacy <c>Logging</c>
    /// shape — collapsed to one in the Phase 1 reshape).
    /// </summary>
    public ICollection<Log> Logs { get; set; } = new List<Log>();
}
