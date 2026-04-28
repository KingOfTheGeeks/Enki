using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Jobs;

/// <summary>
/// Mutable fields for an existing Job. Id is part of the route, not the
/// body. Status is NOT here — transitions go through the dedicated
/// /archive endpoint (and /activate, /complete once those land) so every
/// lifecycle change has a single, audited entry point.
///
/// UnitSystem is editable because the operator may correct an early
/// mis-setup before survey data is loaded. Once scalar measurements are
/// actually in the DB, changing the preset is purely a display preference
/// — the stored SI values don't move — but the service layer may still
/// want to confirm the change is intentional.
/// </summary>
public sealed record UpdateJobDto(
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
    DateTimeOffset? EndTimestamp = null,

    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency.")]
    string? RowVersion = null);
