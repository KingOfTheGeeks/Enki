namespace SDI.Enki.Shared.Runs;

public sealed record RunSummaryDto(
    Guid Id,
    string Name,
    string Description,
    string Type,
    string Status,
    double StartDepth,
    double EndDepth,
    DateTimeOffset? StartTimestamp,
    DateTimeOffset? EndTimestamp);
