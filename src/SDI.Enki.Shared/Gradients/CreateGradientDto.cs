using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Gradients;

/// <summary>
/// Inputs for creating a Gradient under a Run. Validation matches
/// the EF column constraints in
/// <c>TenantDbContext.ConfigureGradient</c> — name capped at 100,
/// numeric ranges sized to the physical envelope of MagTraC tool
/// outputs.
/// </summary>
public sealed record CreateGradientDto(
    [Required(ErrorMessage = "Name is required.")]
    [MaxLength(100, ErrorMessage = "Name must be 100 characters or fewer.")]
    string Name,

    [Required(ErrorMessage = "Order is required.")]
    [Range(0, 10_000, ErrorMessage = "Order must be between 0 and 10,000.")]
    int Order,

    int? ParentId = null,
    DateTime? Timestamp = null,

    [Range(0d, 1_000d, ErrorMessage = "Voltage must be between 0 and 1,000 volts.")]
    double? Voltage = null,

    [Range(0d, 100_000d, ErrorMessage = "Frequency must be between 0 and 100,000 Hz.")]
    double? Frequency = null,

    [Range(0, int.MaxValue, ErrorMessage = "Frame must be non-negative.")]
    int? Frame = null);
