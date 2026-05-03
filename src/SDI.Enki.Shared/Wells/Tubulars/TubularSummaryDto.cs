namespace SDI.Enki.Shared.Wells.Tubulars;

/// <summary>
/// Lightweight row for the tubulars grid. Ordered by
/// <see cref="Order"/> (surface = 0, increasing downward) on the
/// list endpoint. Diameter / weight are the most-shown identity
/// fields so the user can spot-check casing string composition.
///
/// <para>
/// <see cref="FromTvd"/> / <see cref="ToTvd"/> are derived server-side
/// by interpolating MD against the well's Surveys
/// (<c>ISurveyInterpolator</c>, minimum-curvature). Read-only; the
/// authoritative depth on a Tubular is MD. Null if the well has
/// fewer than two surveys.
/// </para>
/// </summary>
public sealed record TubularSummaryDto(
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
    string? RowVersion);
