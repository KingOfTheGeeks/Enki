using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Wells;

/// <summary>
/// Inputs for updating a Well's mutable identity fields. <c>Type</c> is
/// allowed to change because well classification (Target / Intercept /
/// Offset) sometimes flips during a job — e.g. an Offset becomes the
/// new Target after a sidetrack. Surveys and other child rows are
/// updated through their own endpoints.
/// </summary>
public sealed record UpdateWellDto(
    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(200, ErrorMessage = "Name must be 200 characters or fewer.")]
    string Name,

    [Required(ErrorMessage = "Well type is required.")]
    string Type,

    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency.")]
    string? RowVersion);
