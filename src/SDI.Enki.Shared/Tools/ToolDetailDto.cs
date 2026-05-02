namespace SDI.Enki.Shared.Tools;

public sealed record ToolDetailDto(
    Guid Id,
    int SerialNumber,
    string DisplayName,
    string FirmwareVersion,
    string Generation,
    string Status,
    int Configuration,
    int Size,
    int MagnetometerCount,
    int AccelerometerCount,
    string? Notes,
    int CalibrationCount,
    DateTimeOffset? LatestCalibrationDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? RowVersion,

    // ---- Retirement metadata. All null when Status == Active. ----
    string? Disposition,
    DateTimeOffset? RetiredAt,
    string? RetiredBy,
    string? RetirementReason,
    string? RetirementLocation,
    int? ReplacementToolSerial,
    string? ReplacementToolDisplayName);
