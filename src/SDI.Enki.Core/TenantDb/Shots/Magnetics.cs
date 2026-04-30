using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Wells;

namespace SDI.Enki.Core.TenantDb.Shots;

/// <summary>
/// Magnetic reference values (geomagnetic field): total field
/// strength, dip angle, declination. Used in two distinct ways
/// across the schema, both backed by the same table:
///
/// <list type="bullet">
///   <item>
///   <b>Per-well canonical reference</b> — exactly one row per
///   <see cref="Well"/> with <see cref="WellId"/> set. The user
///   curates this on the Well's magnetics page; UI shows it as
///   the well's authoritative reference. Enforced at most one per
///   well by a filtered unique index on <c>WellId</c>.
///   </item>
///   <item>
///   <b>Per-Run owned reference</b> — one row per Run with
///   <see cref="WellId"/> null, pointed at by
///   <c>Run.MagneticsId</c>. Created at run-create time from
///   operator-entered values (no auto-prefill). Two runs can
///   carry identical (BTotal, Dip, Declination) tuples (same well,
///   same operator entry) — there's NO uniqueness on the natural
///   key for these rows; each Run owns its own copy so edits stay
///   local to the Run.
///   </item>
/// </list>
///
/// <para>
/// The pre-issue-#26 schema also had a "per-shot lookup with
/// dedup" pattern (Shot/Log → Magnetics row, deduplicated on
/// natural key) backed by a filtered unique index on
/// <c>(BTotal, Dip, Declination) WHERE WellId IS NULL</c>. That
/// index was dropped when Run-owned Magnetics landed — Shot and
/// Log no longer reference Magnetics at all (the per-run row is
/// the authoritative reference for any captures under the run),
/// and the dedup index would falsely collide every per-run row
/// after the first.
/// </para>
///
/// <para>
/// Storage convention: angles in degrees (signed); BTotal in nano-
/// tesla (the legacy convention — <c>EnkiQuantity.MagneticFluxDensity</c>
/// at the rendering edge handles the projection if other quantities
/// of magnetic field are surfaced elsewhere). Implements
/// <see cref="IAuditable"/> across both shapes.
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
