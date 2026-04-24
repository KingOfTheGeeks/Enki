namespace SDI.Enki.Shared.Shots;

public sealed record ShotDetailDto(
    int Id,
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
    int? GradientId,
    int? RotaryId,
    MagneticsDto? Magnetics,
    CalibrationDto? Calibration);

public sealed record MagneticsDto(int Id, double BTotal, double Dip, double Declination);
public sealed record CalibrationDto(int Id, string Name);
