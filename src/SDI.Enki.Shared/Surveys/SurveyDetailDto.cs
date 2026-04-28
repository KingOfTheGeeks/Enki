namespace SDI.Enki.Shared.Surveys;

/// <summary>
/// Full survey-station projection — observed values, every computed
/// trajectory field, audit, and the optimistic-concurrency token.
/// The detail page round-trips the whole row; the edit DTO accepts
/// only the observed fields + RowVersion (computed values are owned
/// by the auto-calc and rewritten when it runs).
///
/// <para>
/// <see cref="RowVersion"/> is the base64-encoded SQL Server
/// <c>rowversion</c>. Clients must round-trip this value on
/// <see cref="UpdateSurveyDto.RowVersion"/> to perform an edit; a
/// stale value 409s with a reload-and-retry message.
/// </para>
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
    string? UpdatedBy,
    // Optimistic concurrency token
    string? RowVersion);
