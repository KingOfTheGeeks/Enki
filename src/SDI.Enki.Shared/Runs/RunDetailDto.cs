namespace SDI.Enki.Shared.Runs;

public sealed record RunDetailDto(
    Guid Id,
    int JobId,
    string Name,
    string Description,
    string Type,
    string Status,
    double StartDepth,
    double EndDepth,
    DateTimeOffset? StartTimestamp,
    DateTimeOffset? EndTimestamp,
    DateTimeOffset EntityCreated,
    double? BridleLength,
    double? CurrentInjection,
    IReadOnlyList<string> OperatorNames);
