namespace SDI.Enki.Shared.Wells.Formations;

/// <summary>
/// Lightweight row for the formations grid. Ordered by
/// <see cref="FromMeasured"/> on the list endpoint so the layout reads
/// top-down.
///
/// <para>
/// <see cref="FromTvd"/> / <see cref="ToTvd"/> are derived server-side
/// by interpolating MD against the well's Surveys
/// (<c>ISurveyInterpolator</c>, minimum-curvature). Null if the well has
/// fewer than two surveys.
/// </para>
/// </summary>
public sealed record FormationSummaryDto(
    int Id,
    int WellId,
    string Name,
    double FromMeasured,
    double ToMeasured,
    double? FromTvd,
    double? ToTvd,
    double Resistance,
    string? RowVersion);
