namespace SDI.Enki.Shared.Jobs;

/// <summary>
/// Lightweight row for the jobs grid. Region is included because it's a
/// common filter/group column; full detail (created-at, raw timestamps
/// on the archive path, etc.) lives on <see cref="JobDetailDto"/>.
/// </summary>
public sealed record JobSummaryDto(
    Guid Id,
    string Name,
    string? WellName,
    string? Region,
    string Description,
    string Status,
    string UnitSystem,
    DateTimeOffset StartTimestamp,
    DateTimeOffset EndTimestamp);
