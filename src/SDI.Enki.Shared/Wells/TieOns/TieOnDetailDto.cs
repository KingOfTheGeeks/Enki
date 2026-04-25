namespace SDI.Enki.Shared.Wells.TieOns;

/// <summary>
/// Full tie-on projection — observed values, derived grid coordinates,
/// and audit trail. Fed to the edit page so all fields round-trip.
/// </summary>
public sealed record TieOnDetailDto(
    int Id,
    int WellId,
    // Observed
    double Depth,
    double Inclination,
    double Azimuth,
    // Derived / reference grid coordinates
    double North,
    double East,
    double Northing,
    double Easting,
    double VerticalReference,
    double SubSeaReference,
    double VerticalSectionDirection,
    // Audit
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy);
