namespace SDI.Enki.Shared.Wells.TieOns;

/// <summary>
/// Lightweight row for the tie-ons grid. The reference station's
/// observed values (depth + angles) are what the user scans on the
/// list page; computed grid coordinates live on the detail DTO.
/// </summary>
public sealed record TieOnSummaryDto(
    int Id,
    int WellId,
    double Depth,
    double Inclination,
    double Azimuth,
    DateTimeOffset CreatedAt);
