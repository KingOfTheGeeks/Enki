namespace SDI.Enki.Shared.Wells.TieOns;

/// <summary>
/// Full tie-on projection — observed values, derived grid coordinates,
/// audit trail, and the optimistic-concurrency token. Fed to the edit
/// page so all fields round-trip.
///
/// <para>
/// <see cref="RowVersion"/> is the base64-encoded SQL Server
/// <c>rowversion</c>. Clients round-trip this on
/// <see cref="UpdateTieOnDto.RowVersion"/> for optimistic concurrency.
/// </para>
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
    string? UpdatedBy,
    // Optimistic concurrency token
    string? RowVersion);
