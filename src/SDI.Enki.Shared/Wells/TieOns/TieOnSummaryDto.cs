namespace SDI.Enki.Shared.Wells.TieOns;

/// <summary>
/// Row shape for the tie-ons grid. Carries every field on the entity
/// so the list page can show the full data without an extra
/// per-row drilldown.
///
/// <para>
/// <c>North</c> and <c>East</c> are by-definition zero at the tie-on
/// itself (the tie-on is the reference station — its coordinates
/// relative to itself are zero); they're carried in the DTO because
/// downstream Survey calculations consume them and the list view is
/// where the user verifies the full station record.
/// </para>
///
/// <para>
/// <see cref="RowVersion"/> ships on the summary so a list-driven
/// inline edit (or an edit-page navigation) round-trips the
/// optimistic-concurrency token without an extra GET.
/// </para>
/// </summary>
public sealed record TieOnSummaryDto(
    int Id,
    int WellId,
    double Depth,
    double Inclination,
    double Azimuth,
    double North,
    double East,
    double Northing,
    double Easting,
    double VerticalReference,
    double SubSeaReference,
    double VerticalSectionDirection,
    DateTimeOffset CreatedAt,
    string? RowVersion);
