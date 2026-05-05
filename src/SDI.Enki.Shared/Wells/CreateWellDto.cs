using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Wells;

/// <summary>
/// Inputs for creating a Well under a tenant. Validation attributes
/// mirror the column constraints in <c>TenantDbContext.ConfigureWell</c>
/// (<c>Name.MaxLength(200)</c>) so <c>[ApiController]</c>'s automatic
/// ModelState check rejects bad payloads before the DB sees them.
///
/// <c>Type</c> is the <c>WellType</c> SmartEnum name — Target, Intercept,
/// or Offset. The controller resolves it and 400s on an unknown value;
/// these attributes catch the empty/oversized cases.
/// </summary>
public sealed record CreateWellDto(
    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(200, ErrorMessage = "Name must be 200 characters or fewer.")]
    string Name,

    [Required(ErrorMessage = "Well type is required.")]
    string Type);
