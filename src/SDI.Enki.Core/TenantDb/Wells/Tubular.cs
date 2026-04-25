using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Wells.Enums;

namespace SDI.Enki.Core.TenantDb.Wells;

/// <summary>
/// A segment of tubular steel (casing, liner, tubing, drill-pipe, or open hole)
/// inside a Well. Ordered from surface downward by <see cref="Order"/>.
///
/// Implements <see cref="IAuditable"/> — tubular composition gets revised
/// during a job (drillstring trip changes, etc.); audit trail makes those
/// edits visible.
/// </summary>
public class Tubular(int wellId, int order, TubularType type,
    double fromMeasured, double toMeasured, double diameter, double weight) : IAuditable
{
    public int Id { get; set; }

    public int WellId { get; set; } = wellId;

    public string? Name { get; set; }

    /// <summary>Position in the tubular stack — 0 at surface, increasing downward.</summary>
    public int Order { get; set; } = order;

    public TubularType Type { get; set; } = type;

    public double FromMeasured { get; set; } = fromMeasured;
    public double ToMeasured { get; set; } = toMeasured;

    public double Diameter { get; set; } = diameter;
    public double Weight { get; set; } = weight;

    // IAuditable — managed by TenantDbContext.SaveChangesAsync.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }

    // EF nav
    public Well? Well { get; set; }
}
