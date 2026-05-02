namespace SDI.Enki.Core.Master.Settings;

/// <summary>
/// Canonical default values for every <see cref="SystemSettingKeys"/>
/// entry. Single source of truth for the EF seeder and the
/// <c>POST /admin/settings/{key}/reset</c> endpoint — adding a new known
/// key means registering it in <see cref="SystemSettingKeys"/>, adding
/// the default here, and the seeder + reset path both pick it up.
///
/// <para>
/// Values are kept as strings (matching <see cref="SystemSetting.Value"/>);
/// consumers parse per the key's contract. Numeric defaults use
/// invariant culture formatting so they round-trip cleanly through
/// <c>double.TryParse</c> in any locale.
/// </para>
/// </summary>
public static class SystemSettingDefaults
{
    /// <summary>
    /// Returns the seeded default for <paramref name="key"/>. Throws
    /// for unknown keys — callers should gate on
    /// <see cref="SystemSettingKeys.IsKnown"/> first.
    /// </summary>
    public static string Get(string key) => key switch
    {
        SystemSettingKeys.JobRegionSuggestions                 => DefaultRegions,
        SystemSettingKeys.CalibrationDefaultGTotal             => "1000.01",
        SystemSettingKeys.CalibrationDefaultBTotal             => "46895.0",
        SystemSettingKeys.CalibrationDefaultDipDegrees         => "59.867",
        SystemSettingKeys.CalibrationDefaultDeclinationDegrees => "12.313",
        SystemSettingKeys.CalibrationDefaultCoilConstant       => "360.0",
        SystemSettingKeys.CalibrationDefaultActiveBDipDegrees  => "89.44",
        SystemSettingKeys.CalibrationDefaultSampleRateHz       => "100.0",
        SystemSettingKeys.CalibrationDefaultManualSign         => "1.0",
        SystemSettingKeys.CalibrationDefaultCurrent            => "6.01",
        SystemSettingKeys.CalibrationDefaultMagSource          => "static",
        SystemSettingKeys.CalibrationDefaultIncludeDeclination => "true",
        _ => throw new ArgumentException($"Unknown system setting key: '{key}'.", nameof(key)),
    };

    /// <summary>
    /// Default region picker list — one region per line. The seeded
    /// initial value for <see cref="SystemSettingKeys.JobRegionSuggestions"/>.
    /// </summary>
    private const string DefaultRegions =
        "Permian Basin\n" +
        "Bakken\n" +
        "Eagle Ford\n" +
        "Haynesville\n" +
        "Marcellus\n" +
        "North Sea\n" +
        "Gulf of Mexico\n" +
        "Middle East\n" +
        "North Slope\n" +
        "Western Australia";
}
