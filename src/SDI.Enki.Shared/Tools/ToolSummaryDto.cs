namespace SDI.Enki.Shared.Tools;

/// <summary>
/// List-view shape for the master Tools table. Display-only — derived
/// fields like <c>DisplayName</c> ("G2-093") are computed at projection
/// time so the storage stays normalised.
/// </summary>
public sealed record ToolSummaryDto(
    Guid Id,
    int SerialNumber,
    string DisplayName,
    string FirmwareVersion,
    string Generation,
    string Status,
    int MagnetometerCount,
    int AccelerometerCount,
    int CalibrationCount,
    DateTimeOffset? LatestCalibrationDate,
    DateTimeOffset CreatedAt,
    string? RowVersion);
