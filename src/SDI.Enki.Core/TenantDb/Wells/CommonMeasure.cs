using SDI.Enki.Core.Abstractions;

namespace SDI.Enki.Core.TenantDb.Wells;

/// <summary>
/// A depth-ranged dimensionless scaling factor attached to a Well —
/// a "fudge factor" expressed as a percentage of 1 (typically 0.85
/// to 1.15, with 1.0 meaning no adjustment). Consumed by downhole
/// signal-processing algorithms that apply the multiplier per
/// depth interval. Named CommonMeasure in legacy Athena; preserved
/// here with the same shape.
///
/// <para>
/// <see cref="Value"/> is intentionally unitless — no SI quantity is
/// attached and the rendering layer treats it as a bare double. If a
/// future requirement needs CommonMeasure to carry typed quantities
/// (mud weight in kg/m³, temperature in K, etc.), add a Type column
/// and dispatch the EnkiQuantity per row at the rendering edge.
/// </para>
///
/// <para>
/// <b>Depth model.</b> Stored ranges are <i>measured</i> depth
/// (<see cref="FromMeasured"/> / <see cref="ToMeasured"/>) — same
/// rule as Formation. TVD is derived on read by interpolating MD
/// against the well's Surveys via
/// <c>AMR.Core.Survey.ISurveyInterpolator</c> (minimum-curvature),
/// never persisted, never accepted from the wire.
/// </para>
///
/// Implements <see cref="IAuditable"/>.
/// </summary>
public class CommonMeasure(int wellId, double fromMeasured, double toMeasured, double value) : IAuditable
{
    public int Id { get; set; }

    public int WellId { get; set; } = wellId;

    public double FromMeasured { get; set; } = fromMeasured;
    public double ToMeasured { get; set; } = toMeasured;

    public double Value { get; set; } = value;

    // IAuditable — managed by TenantDbContext.SaveChangesAsync.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }

    // EF nav
    public Well? Well { get; set; }
}
