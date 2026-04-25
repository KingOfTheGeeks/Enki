namespace SDI.Enki.Shared.Surveys;

/// <summary>
/// Full survey-station projection — observed values, every computed
/// trajectory field, and audit. The detail page round-trips the whole
/// row; the edit DTO accepts only the observed fields (computed
/// values are owned by Calculate and rewritten when it runs).
/// </summary>
public sealed record SurveyDetailDto(
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
    // Audit
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy);
