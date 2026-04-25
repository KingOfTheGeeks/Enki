namespace SDI.Enki.Shared.Wells;

/// <summary>
/// Lightweight row for the wells grid. Counts of child rows are
/// included so the list page can show "has surveys" / "has tubulars"
/// badges without per-row drill-downs.
/// </summary>
public sealed record WellSummaryDto(
    int Id,
    string Name,
    string Type,
    int SurveyCount,
    int TieOnCount,
    DateTimeOffset CreatedAt);
