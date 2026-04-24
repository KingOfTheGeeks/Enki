namespace SDI.Enki.Shared.Shots;

/// <summary>
/// Create a Shot under a specific Gradient. Magnetics + Calibration
/// payloads are supplied inline; the API resolves each through
/// <c>IEntityLookup.FindOrCreateAsync</c> so identical values across shots
/// collapse to a single lookup row.
/// </summary>
public sealed record CreateGradientShotDto(
    string ShotName,
    DateTimeOffset FileTime,
    int ToolUptime,
    int ShotTime,
    int TimeStart,
    int TimeEnd,
    int NumberOfMags,
    double Frequency,
    double Bandwidth,
    int SampleFrequency,
    int? SampleCount,
    MagneticsInput? Magnetics = null,
    CalibrationInput? Calibration = null);

public sealed record MagneticsInput(double BTotal, double Dip, double Declination);

public sealed record CalibrationInput(string Name, string CalibrationString);
