using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Comments;

/// <summary>
/// Inputs for creating a Comment under a Shot. The parent ShotId
/// is in the URL (<c>POST /shots/{shotId}/comments</c>); the User
/// field is set server-side from the current principal.
/// </summary>
public sealed record CreateCommentDto(
    [Required(ErrorMessage = "Text is required.")]
    [MaxLength(4000, ErrorMessage = "Text must be 4000 characters or fewer.")]
    string Text);
