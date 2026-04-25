using SDI.Enki.Core.Abstractions;

namespace SDI.Enki.Core.TenantDb.Wells;

/// <summary>
/// A geological formation intersected by a Well. Resistance values feed into
/// ranging calculations where surrounding rock conductivity matters.
///
/// Implements <see cref="IAuditable"/> — formation top adjustments during
/// data cleanup are tracked so a downstream calculation surprise can be
/// traced to its source edit.
/// </summary>
public class Formation(int wellId, string name, double fromVertical, double toVertical, double resistance) : IAuditable
{
    public int Id { get; set; }

    public int WellId { get; set; } = wellId;

    public string Name { get; set; } = name;

    public string? Description { get; set; }

    public double FromVertical { get; set; } = fromVertical;
    public double ToVertical { get; set; } = toVertical;

    public double Resistance { get; set; } = resistance;

    // IAuditable — managed by TenantDbContext.SaveChangesAsync.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }

    // EF nav
    public Well? Well { get; set; }
}
