namespace SDI.Enki.Shared.Wells.Formations;

/// <summary>
/// <see cref="FromTvd"/> / <see cref="ToTvd"/> are derived server-side
/// from the well's Surveys via <c>AMR.Core.Survey.ISurveyInterpolator</c>
/// (minimum-curvature) — never persisted on a Formation, never accepted
/// from the wire. Null when the well has fewer than two surveys (so
/// interpolation can't bracket the MD).
/// </summary>
public sealed record FormationDetailDto(
    int Id,
    int WellId,
    string Name,
    string? Description,
    double FromMeasured,
    double ToMeasured,
    double? FromTvd,
    double? ToTvd,
    double Resistance,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    string? RowVersion);
