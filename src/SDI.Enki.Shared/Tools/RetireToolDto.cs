using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Tools;

/// <summary>
/// Body for <c>POST /tools/{serial}/retire</c>. Reason is appended to the
/// tool's Notes field with a timestamp prefix; the lifecycle change itself
/// is the meaningful audit signal (CreatedAt/By + UpdatedAt/By + the new
/// Status value cover the rest).
/// </summary>
public sealed record RetireToolDto(
    [MaxLength(500)] string? Reason = null);
