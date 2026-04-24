namespace SDI.Enki.Core.TenantDb.Jobs;

/// <summary>
/// An offset-well link from this tenant's Job to another Job — typically an
/// archived well in the same tenant, or an active well in a different tenant
/// the caller has read access to. Used for collision avoidance, formation
/// correlation, and passive-magnetic reference in new drilling plans.
///
/// <see cref="ReferencedTenantId"/> is null for same-tenant references, a
/// master-DB <c>Tenant.Id</c> for cross-tenant. <see cref="ReferencedJobId"/>
/// is the target <c>Job.Id</c> — intentionally NOT a SQL FK because the target
/// may be in a different database (cross-tenant or Archive).
///
/// The <see cref="Id"/> surrogate PK stays int — this row is the local link
/// record, not a shared identity, and ReferencedJob rows don't cross tenant
/// boundaries the way Jobs do.
/// </summary>
public class ReferencedJob(Guid jobId, Guid referencedJobId)
{
    public int Id { get; set; }

    public Guid JobId { get; set; } = jobId;

    /// <summary>Master-DB Tenant.Id of the referenced job's tenant; null = same tenant.</summary>
    public Guid? ReferencedTenantId { get; set; }

    /// <summary>Target Job.Id. No SQL FK — may resolve against Archive DB or another tenant.</summary>
    public Guid ReferencedJobId { get; set; } = referencedJobId;

    public string? Purpose { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // EF nav — FK to the owning Job only (the reference target is resolved at runtime)
    public Job? Job { get; set; }
}
