namespace SDI.Enki.Shared.Jobs;

public sealed record JobSummaryDto(
    int Id,
    string Name,
    string? WellName,
    string Description,
    string Status,
    string Units,
    DateTimeOffset StartTimestamp,
    DateTimeOffset EndTimestamp);
