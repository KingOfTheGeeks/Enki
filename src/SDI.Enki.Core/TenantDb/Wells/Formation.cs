using SDI.Enki.Core.Abstractions;

namespace SDI.Enki.Core.TenantDb.Wells;

/// <summary>
/// A geological formation intersected by a Well. Resistance values feed into
/// ranging calculations where surrounding rock conductivity matters.
///
/// <para>
/// <b>Depth model.</b> Stored ranges are <i>measured</i> depth
/// (<see cref="FromMeasured"/> / <see cref="ToMeasured"/>) — the
/// canonical depth in Enki. The <i>vertical</i> depth at a Formation
/// boundary is derived on read by interpolating its MD against the
/// well's Survey stations through <c>AMR.Core.Survey.ISurveyInterpolator</c>
/// (minimum-curvature, same math the Survey VerticalDepth columns use).
/// TVD is never persisted on a Formation and is never accepted from
/// the wire — the surveys are the single source of truth.
/// </para>
///
/// Implements <see cref="IAuditable"/> — formation top adjustments during
/// data cleanup are tracked so a downstream calculation surprise can be
/// traced to its source edit.
/// </summary>
public class Formation(int wellId, string name, double fromMeasured, double toMeasured, double resistance) : IAuditable
{
    public int Id { get; set; }

    public int WellId { get; set; } = wellId;

    public string Name { get; set; } = name;

    public string? Description { get; set; }

    public double FromMeasured { get; set; } = fromMeasured;
    public double ToMeasured { get; set; } = toMeasured;

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
