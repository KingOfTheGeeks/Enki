using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Wells.Enums;

namespace SDI.Enki.Core.TenantDb.Wells;

/// <summary>
/// A wellbore — the physical hole in the ground that Surveys describe.
/// In drilling-survey domain: Target (the one being drilled), Injection
/// (casing used as magnetic reference for Gradient runs), or Offset
/// (existing neighboring well referenced for collision avoidance).
///
/// <para>
/// Wells belong to a single Job (<see cref="JobId"/>, NOT NULL FK with
/// cascade delete). The legacy "a well can be referenced by multiple
/// jobs" idea would land as a future Project concept layered above
/// Job; today's domain is one Well = one Job. Don't carry shared-well
/// assumptions into new code without a project entity to back them.
/// </para>
///
/// <para>
/// Implements <see cref="IAuditable"/> — CreatedAt / CreatedBy /
/// UpdatedAt / UpdatedBy / RowVersion are managed by
/// <c>TenantDbContext.SaveChangesAsync</c>; don't set them from
/// business code.
/// </para>
/// </summary>
public class Well(Guid jobId, string name, WellType type) : IAuditable
{
    public int Id { get; set; }

    public Guid JobId { get; set; } = jobId;

    public string Name { get; set; } = name;

    public WellType Type { get; set; } = type;

    // IAuditable — managed by TenantDbContext.SaveChangesAsync; treat as read-only.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }

    /// <summary>
    /// Soft-delete marker. <c>null</c> means the well is active and
    /// appears in normal queries; non-null means archived — the row
    /// stays in the DB for audit / restore but is hidden by the
    /// global query filter on <c>TenantDbContext</c>. Set by
    /// <c>WellsController.Delete</c>; cleared by
    /// <c>WellsController.Restore</c>.
    /// </summary>
    public DateTimeOffset?  ArchivedAt { get; set; }

    // EF navs
    public Job? Job { get; set; }
    public ICollection<Survey> Surveys { get; set; } = new List<Survey>();
    public ICollection<TieOn> TieOns { get; set; } = new List<TieOn>();
    public ICollection<Tubular> Tubulars { get; set; } = new List<Tubular>();
    public ICollection<Formation> Formations { get; set; } = new List<Formation>();
    public ICollection<CommonMeasure> CommonMeasures { get; set; } = new List<CommonMeasure>();

    /// <summary>
    /// 1:0..1 — the well's canonical geomagnetic reference (BTotal /
    /// Dip / Declination). Reuses the existing
    /// <see cref="Shots.Magnetics"/> entity; per-well rows carry a
    /// non-null <c>WellId</c> and are unique on that column. Per-shot
    /// lookup rows (the legacy dedup pattern) keep <c>WellId</c> null
    /// and aren't reachable through this navigation.
    /// </summary>
    public Shots.Magnetics? Magnetics { get; set; }
}
