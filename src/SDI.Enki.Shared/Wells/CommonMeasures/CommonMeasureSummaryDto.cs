namespace SDI.Enki.Shared.Wells.CommonMeasures;

/// <summary>
/// Lightweight row for the common-measures grid. Ordered by
/// <see cref="FromVertical"/>.
/// </summary>
public sealed record CommonMeasureSummaryDto(
    int Id,
    int WellId,
    double FromVertical,
    double ToVertical,
    double Value);
