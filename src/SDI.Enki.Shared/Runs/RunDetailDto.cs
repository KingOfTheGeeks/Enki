namespace SDI.Enki.Shared.Runs;

/// <summary>
/// Full detail for a single Run. Phase 2 reshape adds:
///
/// <list type="bullet">
///   <item><see cref="ShotCount"/> (alongside the existing
///   <see cref="LogCount"/>) so RunDetail can render Shots + Logs
///   tiles side-by-side without a follow-up query.</item>
///   <item><b>Passive-only</b> capture/calc fields
///   (<c>Has*</c> + <c>Passive*</c> metadata) — populated only when
///   <c>Type == Passive</c>. The actual binary bytes stream via a
///   dedicated download endpoint.</item>
/// </list>
///
/// Carries <see cref="RowVersion"/> for optimistic concurrency.
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
    string? ToolName,
    IReadOnlyList<string> OperatorNames,
    int LogCount,
    int ShotCount,

    // Passive-only capture / calc — null on Gradient and Rotary runs.
    bool HasPassiveBinary,
    string? PassiveBinaryName,
    DateTimeOffset? PassiveBinaryUploadedAt,
    string? PassiveConfigJson,
    DateTimeOffset? PassiveConfigUpdatedAt,
    string? PassiveResultJson,
    DateTimeOffset? PassiveResultComputedAt,
    string? PassiveResultMardukVersion,
    string? PassiveResultStatus,
    string? PassiveResultError,

    string? RowVersion);
