using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Runs.Enums;

namespace SDI.Enki.Core.TenantDb.Runs;

/// <summary>
/// A survey run — Gradient, Rotary, or Passive. Base record for all three types.
/// Run-type-specific fields (BridleLength / CurrentInjection for Gradient, etc.)
/// live in specialization tables introduced in Phase 1c.
/// </summary>
public class Run(string name, string description, double startDepth, double endDepth, RunType type)
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = name;
    public string Description { get; set; } = description;

    public double StartDepth { get; set; } = startDepth;
    public double EndDepth { get; set; } = endDepth;

    public DateTimeOffset? StartTimestamp { get; set; }
    public DateTimeOffset? EndTimestamp { get; set; }

    public DateTimeOffset EntityCreated { get; set; } = DateTimeOffset.UtcNow;

    public RunStatus Status { get; set; } = RunStatus.Planned;
    public RunType Type { get; set; } = type;

    public int JobId { get; set; }

    // EF nav
    public Job? Job { get; set; }
}
