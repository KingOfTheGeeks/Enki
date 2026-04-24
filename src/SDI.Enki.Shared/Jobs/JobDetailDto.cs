namespace SDI.Enki.Shared.Jobs;

/// <summary>
/// Full-fidelity view of a Job. Used by the detail and edit pages — the
/// edit page pre-populates an <see cref="UpdateJobDto"/> from this.
/// EntityCreated is the historical creation timestamp; audit fields
/// (CreatedBy/UpdatedAt/UpdatedBy) land in Phase 6d once Job implements
/// <c>IAuditable</c> and the tenant-DB factory plumbs ICurrentUser.
/// </summary>
public sealed record JobDetailDto(
    int Id,
    string Name,
    string? WellName,
    string? Region,
    string Description,
    string Status,
    string Units,
    DateTimeOffset EntityCreated,
    DateTimeOffset StartTimestamp,
    DateTimeOffset EndTimestamp,
    string? LogoName);
