namespace SDI.Enki.Shared.Calibrations;

/// <summary>
/// Detail-view shape for a single Calibration. The full Marduk payload
/// is returned as <c>PayloadJson</c> (verbatim) so the UI can decide
/// whether to parse + display the raw matrices itself; the Phase-1
/// detail page just shows it pretty-printed in a code block.
/// </summary>
public sealed record CalibrationDetailDto(
    Guid Id,
    Guid ToolId,
    int SerialNumber,
    string ToolDisplayName,
    DateTimeOffset CalibrationDate,
    string? CalibratedBy,
    int MagnetometerCount,
    bool IsNominal,
    bool IsSuperseded,
    string Source,
    string? Notes,
    string PayloadJson,
    DateTimeOffset CreatedAt);
