using SDI.Enki.Core.TenantDb.Runs;

namespace SDI.Enki.Core.TenantDb.Shots;

/// <summary>
/// A passive shot record. Unlike Gradient / Rotary which are parent
/// groupings that own <see cref="Shot"/> rows, Passive is itself the leaf
/// record — it carries shot-positioning fields directly and doesn't need
/// a separate unified Shot table. This matches the legacy Passives shape.
/// </summary>
public class Passive(string name, int order, Guid runId)
{
    public int Id { get; set; }

    public string Name { get; set; } = name;

    public int Order { get; set; } = order;

    public bool IsValid { get; set; } = true;

    public Guid RunId { get; set; } = runId;

    public double AziToTarget { get; set; }
    public double Azimuth { get; set; }
    public double Inclination { get; set; }
    public double MdToTarget { get; set; }
    public double MeasuredDepth { get; set; }
    public double TfToTarget { get; set; }
    public double Toolface { get; set; }

    // EF nav
    public Run? Run { get; set; }
}
