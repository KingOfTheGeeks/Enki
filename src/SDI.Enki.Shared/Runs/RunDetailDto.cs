namespace SDI.Enki.Shared.Runs;

/// <summary>
/// Full detail for a single Run. Carries <see cref="RowVersion"/>
/// for the optimistic-concurrency round-trip and
/// <see cref="LogCount"/> to power the "Logs" tile on the detail
/// page without a follow-up query.
/// </summary>
public sealed record RunDetailDto(
    Guid Id,
    Guid JobId,
    string Name,
    string Description,
    string Type,
    string Status,
    double StartDepth,
    double EndDepth,
    DateTimeOffset? StartTimestamp,
    DateTimeOffset? EndTimestamp,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    double? BridleLength,
    double? CurrentInjection,
    IReadOnlyList<string> OperatorNames,
    int LogCount,
    string? RowVersion);
