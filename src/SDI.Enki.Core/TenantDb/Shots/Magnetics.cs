using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Wells;

namespace SDI.Enki.Core.TenantDb.Shots;

/// <summary>
/// Magnetic reference values (geomagnetic field): total field
/// strength, dip angle, declination. Used in two distinct ways
/// across the schema:
///
/// <list type="bullet">
///   <item>
///   <b>Per-well canonical reference</b> — one row per
///   <see cref="Well"/> with <see cref="WellId"/> set.
///   The user curates this on the Well's magnetics page; UI shows
///   it as the well's authoritative reference. Enforced at most
///   one per well by a unique filtered index on <c>WellId</c>.
///   </item>
///   <item>
///   <b>Per-shot lookup (legacy)</b> — rows with <see cref="WellId"/>
///   null, deduplicated on (BTotal, Dip, Declination) and shared
///   across many <see cref="Shot"/> / <c>Logging</c> records via
///   <c>FindOrCreateAsync</c>. Replaces the legacy
///   <c>trg_ValidateMagnetics</c> AFTER-INSERT trigger. The unique
///   index on (BTotal, Dip, Declination) is filtered to
///   <c>WellId IS NULL</c> so per-well rows don't collide with
///   per-shot lookup rows.
///   </item>
/// </list>
///
/// <para>
/// Storage convention: angles in degrees (signed); BTotal in nano-
/// tesla (the legacy convention — <c>EnkiQuantity.MagneticFluxDensity</c>
/// at the rendering edge handles the projection if other quantities
/// of magnetic field are surfaced elsewhere). Implements
/// <see cref="IAuditable"/> for the per-well usage; lookup rows
/// inherit the same audit columns and stamp them on first creation.
/// </para>
/// </summary>
public class Magnetics(double bTotal, double dip, double declination) : IAuditable
{
    public int Id { get; set; }

    /// <summary>
    /// Optional FK to the owning <see cref="Well"/>. Null on legacy
    /// per-shot lookup rows; non-null + unique-per-well on the
    /// curated per-well rows. The unique-where-not-null constraint
    /// enforces "at most one Magnetics per Well" without preventing
    /// the dedup pattern on the lookup rows.
    /// </summary>
    public int? WellId { get; set; }

    public double BTotal { get; set; } = bTotal;
    public double Dip { get; set; } = dip;
    public double Declination { get; set; } = declination;

    // IAuditable — managed by TenantDbContext.SaveChangesAsync.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }

    // EF nav for the per-well usage. Null on per-shot lookup rows.
    public Well? Well { get; set; }
}
