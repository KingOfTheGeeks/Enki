namespace SDI.Enki.Shared.Runs;

/// <summary>
/// Full detail for a single Run.
///
/// <para>
/// Tool / Calibration / Magnetics fields (issue #26 follow-up):
/// <see cref="ToolId"/> is the assigned tool's master Guid (null
/// before assignment); <see cref="ToolDisplayName"/> renders
/// "{Generation} • {SerialNumber} • {FirmwareVersion}" for the UI.
/// <see cref="SnapshotCalibrationId"/> is the tenant-side snapshot
/// row created when the tool was assigned;
/// <see cref="SnapshotCalibrationDate"/> + display fields render the
/// calibration's metadata without a master DB hit.
/// <see cref="BTotalNanoTesla"/> / <see cref="DipDegrees"/> /
/// <see cref="DeclinationDegrees"/> come from the run's required
/// Magnetics row.
/// </para>
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

    // Tool — null before assignment.
    Guid? ToolId,
    string? ToolDisplayName,

    // Snapshot calibration — null before tool assignment.
    int? SnapshotCalibrationId,
    DateTimeOffset? SnapshotCalibrationDate,
    string? SnapshotCalibrationDisplayName,

    // Magnetics — required, present on every run.
    double BTotalNanoTesla,
    double DipDegrees,
    double DeclinationDegrees,

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
