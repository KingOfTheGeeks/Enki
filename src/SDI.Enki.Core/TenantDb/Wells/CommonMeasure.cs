namespace SDI.Enki.Core.TenantDb.Wells;

/// <summary>
/// A generic depth-ranged scalar measurement attached to a Well — typically
/// a mud property, pore-pressure gradient, or temperature profile. Named
/// CommonMeasure in legacy Athena; preserved here with the same shape.
/// </summary>
public class CommonMeasure(int wellId, double fromVertical, double toVertical, double value)
{
    public int Id { get; set; }

    public int WellId { get; set; } = wellId;

    public double FromVertical { get; set; } = fromVertical;
    public double ToVertical { get; set; } = toVertical;

    public double Value { get; set; } = value;

    // EF nav
    public Well? Well { get; set; }
}
