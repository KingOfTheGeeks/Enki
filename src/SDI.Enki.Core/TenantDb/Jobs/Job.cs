using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.TenantDb.Jobs.Enums;
using SDI.Enki.Core.TenantDb.Runs;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.Units;

namespace SDI.Enki.Core.TenantDb.Jobs;

/// <summary>
/// A drilling project. Every Job belongs to a single tenant (implicit: the
/// database this row lives in). Jobs own Runs and, via ReferencedJob, point
/// at offset wells in other jobs (including archived or cross-tenant).
///
/// Implements <see cref="IAuditable"/> — CreatedAt / CreatedBy / UpdatedAt /
/// UpdatedBy / RowVersion are managed by
/// <c>TenantDbContext.SaveChangesAsync</c>; don't set them from business code.
/// </summary>
public class Job(string name, string description, UnitSystem unitSystem) : IAuditable
{
    /// <summary>
    /// Time-ordered Guid (v7) so the clustered index stays locality-friendly
    /// without the fragmentation penalty of random v4 Guids. Generated on
    /// construction so creating a Job and referencing it in the same unit
    /// of work is trivial — no need to call SaveChanges just to learn the Id.
    /// Guid rather than int because <see cref="ReferencedJob"/> already needs
    /// to point at a Job that may live in another tenant's database; with
    /// Guids an Id is globally unique, with ints you'd need the composite
    /// (TenantId, JobId) everywhere.
    /// </summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = name;

    /// <summary>Optional structured well name — replaces the legacy convention
    /// of embedding client info in <see cref="Name"/>.</summary>
    public string? WellName { get; set; }

    /// <summary>
    /// Free-form geographic region (e.g. "North Sea", "Permian Basin",
    /// "Gulf of Mexico"). Lives on Job rather than Tenant because tenants
    /// are global corporations operating across many regions; work itself
    /// has a location. Intentionally not an enum — the operational
    /// vocabulary is open-ended and changes by client. A canonical
    /// suggestion list (with free-form fallback) can layer on later
    /// without a schema change.
    /// </summary>
    public string? Region { get; set; }

    public string Description { get; set; } = description;

    public DateTimeOffset StartTimestamp { get; set; }
    public DateTimeOffset EndTimestamp { get; set; }

    /// <summary>
    /// Display / input preset for this job. Controls what unit
    /// measurements are shown in (ppg vs kg/m³, ft vs m). Storage
    /// is always canonical SI — this is not a storage format. See
    /// <see cref="UnitSystem"/> and <c>UnitSystemPresets</c>.
    /// </summary>
    public UnitSystem UnitSystem { get; set; } = unitSystem;

    public JobStatus Status { get; set; } = JobStatus.Draft;

    /// <summary>Filename of the job-logo image, when present. Payload stored via Files API.</summary>
    public string? LogoName { get; set; }

    // IAuditable — managed by TenantDbContext.SaveChangesAsync; treat as read-only.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }

    // EF navs
    public ICollection<Run> Runs { get; set; } = new List<Run>();
    public ICollection<Well> Wells { get; set; } = new List<Well>();
    public ICollection<JobUser> Users { get; set; } = new List<JobUser>();
    public ICollection<ReferencedJob> References { get; set; } = new List<ReferencedJob>();
}
