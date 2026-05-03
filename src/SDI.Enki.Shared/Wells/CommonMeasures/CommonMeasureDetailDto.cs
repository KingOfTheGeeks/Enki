namespace SDI.Enki.Shared.Wells.CommonMeasures;

/// <summary>
/// <see cref="FromTvd"/> / <see cref="ToTvd"/> are derived server-side
/// from the well's Surveys via <c>AMR.Core.Survey.ISurveyInterpolator</c>
/// (minimum-curvature). Null when the well has fewer than two surveys.
/// </summary>
public sealed record CommonMeasureDetailDto(
    int Id,
    int WellId,
    double FromMeasured,
    double ToMeasured,
    double? FromTvd,
    double? ToTvd,
    double Value,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    string? RowVersion);
