using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Shots;

/// <summary>
/// Create a Shot under a specific Gradient. Magnetics + Calibration
/// payloads are supplied inline; the API resolves each through
/// <c>IEntityLookup.FindOrCreateAsync</c> so identical values across shots
/// collapse to a single lookup row.
/// </summary>
public sealed record CreateGradientShotDto(
    [Required(ErrorMessage = "Shot name is required.")]
    [MaxLength(100, ErrorMessage = "Shot name must be 100 characters or fewer.")]
    string ShotName,

    [Required(ErrorMessage = "File time is required.")]
    DateTimeOffset FileTime,

    [Required(ErrorMessage = "Tool uptime is required.")]
    [Range(0, int.MaxValue, ErrorMessage = "Tool uptime must be non-negative.")]
    int ToolUptime,

    [Required(ErrorMessage = "Shot time is required.")]
    [Range(0, int.MaxValue, ErrorMessage = "Shot time must be non-negative.")]
    int ShotTime,

    [Required(ErrorMessage = "Time start is required.")]
    [Range(0, int.MaxValue, ErrorMessage = "Time start must be non-negative.")]
    int TimeStart,

    [Required(ErrorMessage = "Time end is required.")]
    [Range(0, int.MaxValue, ErrorMessage = "Time end must be non-negative.")]
    int TimeEnd,

    [Required(ErrorMessage = "Number of mags is required.")]
    [Range(0, 1_000, ErrorMessage = "Number of mags must be between 0 and 1,000.")]
    int NumberOfMags,

    [Required(ErrorMessage = "Frequency is required.")]
    [Range(0d, 100_000d, ErrorMessage = "Frequency must be between 0 and 100,000 Hz.")]
    double Frequency,

    [Required(ErrorMessage = "Bandwidth is required.")]
    [Range(0d, 100_000d, ErrorMessage = "Bandwidth must be between 0 and 100,000 Hz.")]
    double Bandwidth,

    [Required(ErrorMessage = "Sample frequency is required.")]
    [Range(0, int.MaxValue, ErrorMessage = "Sample frequency must be non-negative.")]
    int SampleFrequency,

    [Range(0, int.MaxValue, ErrorMessage = "Sample count must be non-negative.")]
    int? SampleCount,
    MagneticsInput? Magnetics = null,
    CalibrationInput? Calibration = null);

/// <summary>
/// Magnetic-reference triple supplied with a Shot. Bounds match
/// the per-well <c>SetMagneticsDto</c> so the inline shot lookup
/// and the per-well row use the same physical envelope.
/// </summary>
public sealed record MagneticsInput(
    [Range(0d, 100_000d, ErrorMessage = "Total field must be between 0 and 100,000 nT.")]
    double BTotal,

    [Range(-90d, 90d, ErrorMessage = "Dip must be between -90 and 90 degrees.")]
    double Dip,

    [Range(-180d, 180d, ErrorMessage = "Declination must be between -180 and 180 degrees.")]
    double Declination);

/// <summary>
/// Calibration-string triple supplied with a Shot.
/// </summary>
public sealed record CalibrationInput(
    [Required(ErrorMessage = "Calibration name is required.")]
    [MaxLength(100, ErrorMessage = "Calibration name must be 100 characters or fewer.")]
    string Name,

    [Required(ErrorMessage = "Calibration string is required.")]
    [MaxLength(4000, ErrorMessage = "Calibration string must be 4,000 characters or fewer.")]
    string CalibrationString);
