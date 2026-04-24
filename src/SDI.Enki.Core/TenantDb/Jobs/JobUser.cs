namespace SDI.Enki.Core.TenantDb.Jobs;

/// <summary>
/// Assigns a master-DB User to a Job. Composite PK (JobId, UserId).
/// <see cref="UserId"/> references master.Users.Id but has no SQL FK
/// because the User lives in a different database — integrity is
/// enforced at the application layer (tenant-routing + repository).
/// </summary>
public class JobUser(Guid jobId, Guid userId)
{
    public Guid JobId { get; set; } = jobId;

    /// <summary>Guid of the master-DB User. No SQL FK — cross-database.</summary>
    public Guid UserId { get; set; } = userId;

    // EF nav (Job side only — User lives in master)
    public Job? Job { get; set; }
}
