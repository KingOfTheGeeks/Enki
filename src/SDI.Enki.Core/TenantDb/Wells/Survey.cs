namespace SDI.Enki.Core.TenantDb.Wells;

/// <summary>
/// A single survey station for a Well — raw observations (Depth / Inclination /
/// Azimuth) plus calculated trajectory values (VerticalDepth, SubSea, North,
/// East, DoglegSeverity, VerticalSection, Northing, Easting, Build, Turn).
///
/// The calculated fields are populated by Marduk's <c>ISurveyCalculator.Process</c>
/// using the minimum-curvature method. Enki persists the results; it does not
/// reimplement the math.
/// </summary>
public class Survey(int wellId, double depth, double inclination, double azimuth)
{
    public int Id { get; set; }

    public int WellId { get; set; } = wellId;

    // Input
    public double Depth { get; set; } = depth;
    public double Inclination { get; set; } = inclination;
    public double Azimuth { get; set; } = azimuth;

    // Calculated by Marduk ISurveyCalculator
    public double VerticalDepth { get; set; }
    public double SubSea { get; set; }
    public double North { get; set; }
    public double East { get; set; }
    public double DoglegSeverity { get; set; }
    public double VerticalSection { get; set; }
    public double Northing { get; set; }
    public double Easting { get; set; }
    public double Build { get; set; }
    public double Turn { get; set; }

    // EF nav
    public Well? Well { get; set; }
}
