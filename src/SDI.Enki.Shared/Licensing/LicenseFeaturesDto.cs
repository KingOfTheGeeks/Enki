namespace SDI.Enki.Shared.Licensing;

/// <summary>
/// Feature flags baked into a generated license. Field names match
/// Marduk's <c>AMR.Core.Licensing.Domain.Models.LicenseFeatureFlags</c>
/// exactly so the generated payload deserialises into Marduk's typed
/// <c>LicensePayload</c> on the consumer side.
/// </summary>
public sealed record LicenseFeaturesDto(
    bool AllowWarrior        = false,
    bool AllowNorthSea       = false,
    bool AllowSerial         = false,
    bool AllowRotary         = false,
    bool AllowGradient       = false,
    bool AllowPassive        = false,
    bool AllowWarriorLogging = false,
    bool AllowCalibrate      = false,
    bool AllowSurvey         = false,
    bool AllowResults        = false,
    bool AllowGyro           = false);
