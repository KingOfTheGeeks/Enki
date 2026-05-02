using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Tools;

/// <summary>
/// Body for <c>POST /tools/{serial}/retire</c>. Captures structured
/// retirement metadata: disposition, effective date, reason, and the
/// optional replacement tool + final location. Persists to discrete
/// columns on <c>Tool</c> so retired-fleet reports can filter and
/// group without parsing free-text Notes. The tool's <c>Status</c> is
/// derived server-side from <see cref="Disposition"/> (only
/// <c>Lost</c> flips to <c>ToolStatus.Lost</c>; everything else flips
/// to <c>ToolStatus.Retired</c>).
/// </summary>
public sealed record RetireToolDto(
    [Required(ErrorMessage = "Disposition is required.")]
    string? Disposition,
    [Required(ErrorMessage = "Effective date is required.")]
    DateOnly? EffectiveDate,
    [Required(ErrorMessage = "Reason is required.")]
    [MaxLength(500)]
    string? Reason,
    int? ReplacementToolSerial,
    [MaxLength(200)]
    string? FinalLocation,
    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency.")]
    string? RowVersion);
