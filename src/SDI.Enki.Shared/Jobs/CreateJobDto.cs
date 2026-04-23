namespace SDI.Enki.Shared.Jobs;

public sealed record CreateJobDto(
    string Name,
    string Description,
    string Units,
    string? WellName = null,
    DateTimeOffset? StartTimestamp = null,
    DateTimeOffset? EndTimestamp = null);
