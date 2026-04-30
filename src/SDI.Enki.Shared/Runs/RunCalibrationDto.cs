namespace SDI.Enki.Shared.Runs;

/// <summary>
/// Calibration option for the per-Run calibration dropdown
/// (used by <c>ShotEdit</c> / <c>LogEdit</c> to populate the
/// "calibration to use" select). Sourced from tenant snapshots —
/// each row is a copy of a master Calibration that's been pulled
/// in for this run's tool.
///
/// <para>
/// <see cref="DisplayName"/> is the pre-formatted label the
/// dropdown renders ("yyyy-MM-dd • SN {serial}"); the page just
/// binds <see cref="Id"/> to the form field.
/// </para>
/// </summary>
public sealed record RunCalibrationDto(
    int Id,
    DateTimeOffset CalibrationDate,
    int SerialNumber,
    string DisplayName,
    bool IsNominal);
