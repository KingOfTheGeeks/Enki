using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Licensing;

public sealed record RevokeLicenseDto(
    [Required, MaxLength(500)] string Reason);
