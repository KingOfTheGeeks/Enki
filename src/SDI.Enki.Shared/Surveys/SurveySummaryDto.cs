namespace SDI.Enki.Shared.Surveys;

/// <summary>
/// Row shape for the surveys grid. Carries every field on the entity
/// — the three observed values (depth / inclination / azimuth) plus
/// every computed trajectory field — so the list page shows the full
/// station record without an extra per-row drilldown.
///
/// <para>
/// Computed columns are zero on freshly-inserted rows; they populate
/// after Marduk's minimum-curvature engine runs via the auto-calc.
/// </para>
///
/// <para>
/// <see cref="RowVersion"/> ships on the summary so a list-driven
/// inline edit (or an edit-page navigation) round-trips the
/// optimistic-concurrency token without an extra GET. Base64-encoded
/// SQL Server <c>rowversion</c>; see <see cref="SurveyDetailDto"/>.
/// </para>
/// </summary>
public sealed record SurveySummaryDto(
    int Id,
    int WellId,
    // Observed
    double Depth,
    double Inclination,
    double Azimuth,
    // Computed by Marduk's minimum-curvature engine
    double VerticalDepth,
    double SubSea,
    double North,
    double East,
    double DoglegSeverity,
    double VerticalSection,
    double Northing,
    double Easting,
    double Build,
    double Turn,
    // Optimistic concurrency token
    string? RowVersion);
