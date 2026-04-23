namespace SDI.Enki.Core.TenantDb.Wells;

/// <summary>
/// Reference station for a Well — the starting point for minimum-curvature
/// trajectory calculation. A Well has one active TieOn at any time; additional
/// TieOn rows may exist for historical / alternative references.
/// </summary>
public class TieOn(int wellId, double depth, double inclination, double azimuth)
{
    public int Id { get; set; }

    public int WellId { get; set; } = wellId;

    public double Depth { get; set; } = depth;
    public double Inclination { get; set; } = inclination;
    public double Azimuth { get; set; } = azimuth;

    public double North { get; set; }
    public double East { get; set; }
    public double Northing { get; set; }
    public double Easting { get; set; }
    public double VerticalReference { get; set; }
    public double SubSeaReference { get; set; }
    public double VerticalSectionDirection { get; set; }

    // EF nav
    public Well? Well { get; set; }
}
