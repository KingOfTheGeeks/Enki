namespace SDI.Enki.Shared.Surveys;

/// <summary>
/// Lightweight row for the surveys grid. Carries the three observed
/// values (depth / inclination / azimuth) plus the two most-shown
/// computed columns (VerticalDepth and DoglegSeverity) so the grid
/// reads usefully even before the user clicks Calculate. Full
/// computed-trajectory fields land on
/// <see cref="SurveyDetailDto"/> for the per-row drilldown.
/// </summary>
public sealed record SurveySummaryDto(
    int Id,
    int WellId,
    double Depth,
    double Inclination,
    double Azimuth,
    double VerticalDepth,
    double DoglegSeverity);
