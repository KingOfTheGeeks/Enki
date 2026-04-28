namespace SDI.Enki.Shared.Runs;

/// <summary>
/// Run row in a Job's runs list. Carries <see cref="RowVersion"/>
/// (base64) so inline-edit grids can round-trip the optimistic-
/// concurrency token without an extra GET, and
/// <see cref="LogCount"/> so the list cell can show "N logs"
/// without a follow-up join.
/// </summary>
public sealed record RunSummaryDto(
    Guid Id,
    string Name,
    string Description,
    string Type,
    string Status,
    double StartDepth,
    double EndDepth,
    DateTimeOffset? StartTimestamp,
    DateTimeOffset? EndTimestamp,
    int LogCount,
    string? RowVersion);
