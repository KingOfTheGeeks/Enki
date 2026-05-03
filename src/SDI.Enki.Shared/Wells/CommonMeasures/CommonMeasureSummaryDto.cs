namespace SDI.Enki.Shared.Wells.CommonMeasures;

/// <summary>
/// Lightweight row for the common-measures grid. Ordered by
/// <see cref="FromMeasured"/>.
///
/// <para>
/// <see cref="FromTvd"/> / <see cref="ToTvd"/> are derived server-side
/// by interpolating MD against the well's Surveys
/// (<c>ISurveyInterpolator</c>, minimum-curvature). Null if the well has
/// fewer than two surveys.
/// </para>
/// </summary>
public sealed record CommonMeasureSummaryDto(
    int Id,
    int WellId,
    double FromMeasured,
    double ToMeasured,
    double? FromTvd,
    double? ToTvd,
    double Value,
    string? RowVersion);
