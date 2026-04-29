using SDI.Enki.Core.Abstractions;

namespace SDI.Enki.Core.Master.Settings;

/// <summary>
/// App-wide configuration value managed at runtime through the admin
/// UI. Distinct from <see cref="Setting"/> (which is the legacy
/// user-scoped export-profile blob) — system settings are global,
/// untyped strings keyed by a stable name. Consumers parse the value
/// according to a known shape per key.
///
/// <para>
/// Known keys are listed in <c>SystemSettingKeys</c>; unknown keys are
/// rejected at the write endpoint so the table can't silently grow
/// unmanaged values.
/// </para>
///
/// <para>
/// Implements <see cref="IAuditable"/> so we know who last touched
/// each setting + when. Particularly relevant for any setting that
/// affects security posture (allowed regions, default unit systems,
/// future feature flags).
/// </para>
/// </summary>
public class SystemSetting(string key, string value) : IAuditable
{
    public int Id { get; set; }

    /// <summary>Stable, dot-separated key (e.g. "Jobs:RegionSuggestions").</summary>
    public string Key { get; set; } = key;

    /// <summary>Raw string value. Consumers parse per the key's contract.</summary>
    public string Value { get; set; } = value;

    // IAuditable — managed by the master DbContext interceptor.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }
}

/// <summary>
/// Allowlist of system-setting keys. Anything not in this list is
/// rejected at the write endpoint so the table can't silently
/// accumulate unmanaged values via a typo.
/// </summary>
public static class SystemSettingKeys
{
    /// <summary>
    /// Suggestions for the Region picker on Job create / edit. Stored
    /// as one region per line; consumers split by newline. An admin
    /// can update this list without a deploy when SDI moves into a new
    /// operating area.
    /// </summary>
    public const string JobRegionSuggestions = "Jobs:RegionSuggestions";

    // Calibration reference field defaults — shipped values from Nabu's
    // settings.json. Per-calibration overrides happen in the wizard;
    // these are what the form pre-populates on first load. Each is a
    // single string value parsed by the consumer (CalibrationProcessing
    // service).
    public const string CalibrationDefaultGTotal             = "Calibration:Default:GTotal";
    public const string CalibrationDefaultBTotal             = "Calibration:Default:BTotal";
    public const string CalibrationDefaultDipDegrees         = "Calibration:Default:DipDegrees";
    public const string CalibrationDefaultDeclinationDegrees = "Calibration:Default:DeclinationDegrees";
    public const string CalibrationDefaultCoilConstant       = "Calibration:Default:CoilConstant";
    public const string CalibrationDefaultActiveBDipDegrees  = "Calibration:Default:ActiveBDipDegrees";
    public const string CalibrationDefaultSampleRateHz       = "Calibration:Default:SampleRateHz";
    public const string CalibrationDefaultManualSign         = "Calibration:Default:ManualSign";
    public const string CalibrationDefaultCurrent            = "Calibration:Default:Current";
    public const string CalibrationDefaultMagSource          = "Calibration:Default:MagSource";
    public const string CalibrationDefaultIncludeDeclination = "Calibration:Default:IncludeDeclination";

    /// <summary>Iteration helper for the admin UI.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        JobRegionSuggestions,
        CalibrationDefaultGTotal,
        CalibrationDefaultBTotal,
        CalibrationDefaultDipDegrees,
        CalibrationDefaultDeclinationDegrees,
        CalibrationDefaultCoilConstant,
        CalibrationDefaultActiveBDipDegrees,
        CalibrationDefaultSampleRateHz,
        CalibrationDefaultManualSign,
        CalibrationDefaultCurrent,
        CalibrationDefaultMagSource,
        CalibrationDefaultIncludeDeclination,
    ];

    public static bool IsKnown(string key) => All.Contains(key);
}
