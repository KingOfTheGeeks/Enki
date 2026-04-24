using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Jobs;

/// <summary>
/// Inputs for creating a Job inside a tenant. Validation attributes mirror
/// the TenantDbContext column constraints so ASP.NET Core's automatic
/// ModelState check rejects bad payloads before the DB ever sees them
/// (and so Blazor's EditForm can surface the same messages client-side).
///
/// <c>UnitSystem</c> is a string because the DTO crosses the wire as
/// JSON — the controller resolves it to the <c>UnitSystem</c> SmartEnum
/// and 400s on an unknown value. Expected: <c>"Field"</c>, <c>"Metric"</c>,
/// or <c>"SI"</c>.
///
/// Status is not settable on create: new jobs always start as
/// <c>JobStatus.Draft</c>; status transitions happen via the separate
/// /archive (and later /activate, /complete) endpoints.
/// </summary>
public sealed record CreateJobDto(
    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(50, ErrorMessage = "Name must be 50 characters or fewer.")]
    string Name,

    [Required(ErrorMessage = "Description is required.")]
    [MaxLength(200, ErrorMessage = "Description must be 200 characters or fewer.")]
    string Description,

    [Required(ErrorMessage = "Unit system is required.")]
    string UnitSystem,

    [MaxLength(100, ErrorMessage = "Well name must be 100 characters or fewer.")]
    string? WellName = null,

    [MaxLength(64, ErrorMessage = "Region must be 64 characters or fewer.")]
    string? Region = null,

    DateTimeOffset? StartTimestamp = null,
    DateTimeOffset? EndTimestamp = null);
