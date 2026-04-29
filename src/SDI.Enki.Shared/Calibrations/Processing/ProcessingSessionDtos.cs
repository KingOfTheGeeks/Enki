using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Calibrations.Processing;

/// <summary>
/// State of a calibration processing session — server-side state machine
/// driven by <c>POST /process</c> → <c>POST /compute</c> → <c>POST /save</c>.
/// The wizard polls <see cref="ProcessingSessionStatusDto"/> while the
/// background parse + NarrowBand pass runs.
/// </summary>
public enum ProcessingSessionState
{
    /// <summary>Session created, background parse + NarrowBand in flight.</summary>
    Parsing,
    /// <summary>All 24 shots parsed; operator picks selections then triggers Compute.</summary>
    ReadyForCompute,
    /// <summary>Marduk compute in flight.</summary>
    Computing,
    /// <summary>Compute finished; operator reviews diagnostics and saves or discards.</summary>
    Computed,
    /// <summary>Calibration row written; session terminal.</summary>
    Saved,
    /// <summary>Session terminal — see <c>Error</c>.</summary>
    Failed,
}

/// <summary>
/// Polled by <c>ToolCalibrate.razor</c> every ~2s. Carries per-shot
/// completion so the wizard can render a 24-row progress table while the
/// background pipeline runs.
/// </summary>
public sealed record ProcessingSessionStatusDto(
    Guid                            SessionId,
    int                             ToolSerial,
    string                          State,                  // ProcessingSessionState.ToString()
    int                             ShotsParsed,            // 0..24
    IReadOnlyList<ProcessingShotPreviewDto> Previews,       // length = ShotsParsed (or 24 once done)
    ProcessingResultDto?            Result,                 // populated when State = Computed/Saved
    Guid?                           SavedCalibrationId,     // populated when State = Saved
    string?                         Error);                 // populated when State = Failed

/// <summary>
/// Lightweight per-shot info for the operator to decide enablement +
/// magnetometer source. Skips Nabu's elaborate per-mag azimuth/MTF
/// computation — operators pick on residuals + frequency stability.
/// </summary>
public sealed record ProcessingShotPreviewDto(
    int    ShotIndex,           // 1-based (matches the binary file name)
    int    SampleCount,
    double FrequencyHz,         // F0 fitted from NarrowBand
    double TemperatureC,
    double GravityMagnitude,    // |G| in mG (sanity vs reference)
    IReadOnlyList<double> PerMagAcollinearity);  // per-mag acollinearity diagnostic

public sealed record ProcessingComputeRequestDto(
    [Required, MinLength(1)] IReadOnlyList<int> EnabledShotIndices,    // 1-based
    [Required, Range(0d, double.MaxValue)] double GTotal,
    [Required, Range(0d, double.MaxValue)] double BTotal,
    [Required, Range(-180d, 180d)] double DipDegrees,
    [Required, Range(-180d, 180d)] double DeclinationDegrees,
    [Required, Range(0d, double.MaxValue)] double CoilConstant,
    [Required, Range(-180d, 180d)] double ActiveBDipDegrees,
    [Required, Range(0.001d, 100_000d)] double SampleRateHz,
    [Required, Range(-1d, 1d)] double ManualSign,
    [Required] IReadOnlyList<double> CurrentsByShot);  // length 24

public sealed record ProcessingResultDto(
    bool                                        Success,
    double                                      GravityResidual,        // RMS mG
    IReadOnlyList<double>                       MagneticResiduals,      // per-mag RMS nT
    IReadOnlyList<string>                       Report,                 // Marduk message log
    IReadOnlyList<ProcessingShotDiagnosticDto>  ShotDiagnostics,        // per-shot post-compute
    string?                                     Error);

/// <summary>Per-shot post-compute diagnostic — calibrated G + per-mag B.</summary>
public sealed record ProcessingShotDiagnosticDto(
    int    ShotIndex,
    bool   Enabled,
    double CalibratedGTotal,
    IReadOnlyList<double> CalibratedBTotalPerMag);

public sealed record ProcessingSaveRequestDto(
    [Required, MaxLength(200)] string CalibrationName,
    [MaxLength(200)] string? CalibratedBy,
    [MaxLength(1000)] string? Notes);

public sealed record ProcessingSaveResultDto(
    Guid CalibrationId,
    Guid? SupersededCalibrationId);
