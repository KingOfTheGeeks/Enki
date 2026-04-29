namespace SDI.Enki.Shared.Calibrations.Processing;

/// <summary>
/// Reference-field defaults that pre-populate the ToolCalibrate wizard
/// form. Backed by SystemSetting rows with keys under
/// <c>SystemSettingKeys.CalibrationDefault*</c>; an admin can edit them
/// from the System Settings page without a redeploy.
/// </summary>
public sealed record ProcessingDefaultsDto(
    double GTotal,
    double BTotal,
    double DipDegrees,
    double DeclinationDegrees,
    double CoilConstant,
    double ActiveBDipDegrees,
    double SampleRateHz,
    double ManualSign,
    double DefaultCurrent,
    string MagSource,
    bool   IncludeDeclination);
