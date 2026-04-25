using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Surveys;

/// <summary>
/// Bulk-create payload for posting many survey stations at once.
/// Wraps an array of <see cref="CreateSurveyDto"/>; the controller
/// validates depth-monotonicity at the bulk level (Marduk's
/// minimum-curvature engine expects strictly-increasing depth).
///
/// <para>
/// Atomic: the controller wraps the inserts in a single
/// <c>SaveChangesAsync</c>, so a row-level validation failure
/// rolls the whole batch back. Partial-success "X of N inserted"
/// semantics are deliberately not supported — that pattern hides
/// data shape bugs.
/// </para>
/// </summary>
public sealed record CreateSurveysDto(
    [Required(ErrorMessage = "At least one survey station is required.")]
    [MinLength(1, ErrorMessage = "At least one survey station is required.")]
    IReadOnlyList<CreateSurveyDto> Stations);
