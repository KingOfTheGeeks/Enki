using SDI.Enki.Core.Abstractions;

namespace SDI.Enki.Core.TenantDb.Wells;

/// <summary>
/// Reference station for a Well — the starting point for minimum-curvature
/// trajectory calculation. A Well has one active TieOn at any time; additional
/// TieOn rows may exist for historical / alternative references.
///
/// Implements <see cref="IAuditable"/> — audit columns let an operator see
/// when a tie-on was last revised, which matters because changing the tie-on
/// invalidates every previously-calculated Survey on the well.
/// </summary>
public class TieOn(int wellId, double depth, double inclination, double azimuth) : IAuditable
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

    // IAuditable — managed by TenantDbContext.SaveChangesAsync.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }

    // EF nav
    public Well? Well { get; set; }
}
