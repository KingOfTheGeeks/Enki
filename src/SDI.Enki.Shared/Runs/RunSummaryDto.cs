namespace SDI.Enki.Shared.Runs;

/// <summary>
/// Run row in a Job's runs list. Carries <see cref="RowVersion"/>
/// for optimistic concurrency, <see cref="LogCount"/> + the new
/// <see cref="ShotCount"/> so the list cell can show
/// "N logs / M shots" without a follow-up join.
///
/// <para>
/// <see cref="ToolDisplayName"/> shows "{Generation} • {SerialNumber}"
/// when assigned and "(no tool)" otherwise so the list communicates
/// at a glance which runs are still missing their tool.
/// </para>
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
    Guid? ToolId,
    string? ToolDisplayName,
    int LogCount,
    int ShotCount,
    string? RowVersion);
