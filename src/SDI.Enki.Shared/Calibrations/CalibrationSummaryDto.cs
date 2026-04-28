namespace SDI.Enki.Shared.Calibrations;

/// <summary>
/// List-view shape for the master Calibrations table. <c>IsNominal</c> and
/// <c>IsSuperseded</c> are denormalised columns so list filters don't have
/// to parse the full Marduk payload.
/// </summary>
public sealed record CalibrationSummaryDto(
    Guid Id,
    Guid ToolId,
    int SerialNumber,
    string ToolDisplayName,
    DateTimeOffset CalibrationDate,
    string? CalibratedBy,
    int MagnetometerCount,
    bool IsNominal,
    bool IsSuperseded,
    string Source);
