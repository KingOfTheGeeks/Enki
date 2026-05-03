namespace SDI.Enki.Shared.Wells.Tubulars;

/// <summary>
/// Full tubular projection — every observed field plus audit. The
/// edit page round-trips this so an unchanged field stays intact on
/// PUT.
///
/// <para>
/// <see cref="FromTvd"/> / <see cref="ToTvd"/> are derived server-side
/// by interpolating MD against the well's Surveys
/// (<c>ISurveyInterpolator</c>, minimum-curvature). Read-only; null
/// when the well has fewer than two surveys.
/// </para>
/// </summary>
public sealed record TubularDetailDto(
    int Id,
    int WellId,
    string? Name,
    int Order,
    string Type,
    double FromMeasured,
    double ToMeasured,
    double? FromTvd,
    double? ToTvd,
    double Diameter,
    double Weight,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    string? RowVersion);
