namespace SDI.Enki.Shared.Shots;

public sealed record ShotSummaryDto(
    int Id,
    string ShotName,
    DateTimeOffset FileTime,
    double Frequency,
    int? GradientId,
    int? RotaryId);
