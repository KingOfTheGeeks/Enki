using SDI.Enki.Core.TenantDb.Jobs;
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

    // Gradient-specific — nullable because Rotary and Passive runs don't set them.
    public double? BridleLength { get; set; }
    public double? CurrentInjection { get; set; }

    // EF navs
    public Job? Job { get; set; }
    public ICollection<Operator> Operators { get; set; } = new List<Operator>();
    public ICollection<Gradient> Gradients { get; set; } = new List<Gradient>();
    public ICollection<Rotary> Rotaries { get; set; } = new List<Rotary>();
    public ICollection<Passive> Passives { get; set; } = new List<Passive>();
}
