namespace SDI.Enki.Shared.Jobs;

/// <summary>
/// Full-fidelity view of a Job. Used by the detail and edit pages — the
/// edit page pre-populates an <see cref="UpdateJobDto"/> from this.
///
/// Audit fields (CreatedAt / CreatedBy / UpdatedAt / UpdatedBy) come from
/// the tenant-DB audit interceptor on save. The wire DTO carries them so
/// "who created this and when" is visible on the detail page without a
/// second round-trip.
/// </summary>
public sealed record JobDetailDto(
    Guid Id,
    string Name,
    string? WellName,
    string? Region,
    string Description,
    string Status,
    string UnitSystem,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    DateTimeOffset StartTimestamp,
    DateTimeOffset EndTimestamp,
    string? LogoName);
