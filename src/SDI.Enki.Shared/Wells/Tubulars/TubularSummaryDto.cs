namespace SDI.Enki.Shared.Wells.Tubulars;

/// <summary>
/// Lightweight row for the tubulars grid. Ordered by
/// <see cref="Order"/> (surface = 0, increasing downward) on the
/// list endpoint. Diameter / weight are the most-shown identity
/// fields so the user can spot-check casing string composition.
/// </summary>
public sealed record TubularSummaryDto(
    int Id,
    int WellId,
    string? Name,
    int Order,
    string Type,
    double FromMeasured,
    double ToMeasured,
    double Diameter,
    double Weight,
    string? RowVersion);
