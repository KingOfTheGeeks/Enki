namespace SDI.Enki.Shared.Wells.Tubulars;

/// <summary>
/// Full tubular projection — every observed field plus audit. The
/// edit page round-trips this so an unchanged field stays intact on
/// PUT.
/// </summary>
public sealed record TubularDetailDto(
    int Id,
    int WellId,
    string? Name,
    int Order,
    string Type,
    double FromMeasured,
    double ToMeasured,
    double Diameter,
    double Weight,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    string? RowVersion);
