using SDI.Enki.Core.TenantDb.Jobs.Enums;
using SDI.Enki.Core.TenantDb.Runs;

namespace SDI.Enki.Core.TenantDb.Jobs;

/// <summary>
/// A drilling project. Every Job belongs to a single tenant (implicit: the
/// database this row lives in). Jobs own Runs and, via ReferencedJob, point
/// at offset wells in other jobs (including archived or cross-tenant).
/// </summary>
public class Job(string name, string description, Units units)
{
    public int Id { get; set; }

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

    public DateTimeOffset EntityCreated { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset StartTimestamp { get; set; }
    public DateTimeOffset EndTimestamp { get; set; }

    public Units Units { get; set; } = units;
    public JobStatus Status { get; set; } = JobStatus.Draft;

    /// <summary>Filename of the job-logo image, when present. Payload stored via Files API.</summary>
    public string? LogoName { get; set; }

    // EF navs
    public ICollection<Run> Runs { get; set; } = new List<Run>();
    public ICollection<JobUser> Users { get; set; } = new List<JobUser>();
    public ICollection<ReferencedJob> References { get; set; } = new List<ReferencedJob>();
}
