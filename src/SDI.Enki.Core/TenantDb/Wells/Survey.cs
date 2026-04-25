using SDI.Enki.Core.Abstractions;

namespace SDI.Enki.Core.TenantDb.Wells;

/// <summary>
/// A single survey station for a Well — raw observations (Depth / Inclination /
/// Azimuth) plus calculated trajectory values (VerticalDepth, SubSea, North,
/// East, DoglegSeverity, VerticalSection, Northing, Easting, Build, Turn).
///
/// The calculated fields are populated by Marduk's <c>ISurveyCalculator.Process</c>
/// using the minimum-curvature method. Enki persists the results; it does not
/// reimplement the math.
///
/// Implements <see cref="IAuditable"/> — audit columns track who edited a
/// station reading and when. Edits are common during data cleanup so audit
/// trail matters here even though the parent Well's audit is the bigger
/// signal.
/// </summary>
public class Survey(int wellId, double depth, double inclination, double azimuth) : IAuditable
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

    // IAuditable — managed by TenantDbContext.SaveChangesAsync.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }

    // EF nav
    public Well? Well { get; set; }
}
