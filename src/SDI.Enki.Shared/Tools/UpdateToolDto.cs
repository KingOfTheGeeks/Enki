using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Tools;

/// <summary>
/// Mutable fields on an existing tool. Tools get refurbished, reconfigured,
/// firmware-updated and sensor-swapped over their service life — no field
/// here is intrinsic to the unit. Status transitions live on the separate
/// <c>/retire</c> and <c>/reactivate</c> endpoints; that's the only carve-
/// out (lifecycle is a verb, not a field edit).
///
/// SerialNumber is editable because refurb can re-issue serials; the PUT
/// endpoint handles the unique-index collision and the UI redirects to
/// the new <c>/tools/{serial}</c> after a rename.
/// </summary>
public sealed record UpdateToolDto(
    [Required, Range(1, int.MaxValue, ErrorMessage = "Serial must be a positive integer.")]
    int SerialNumber,

    [Required, MaxLength(64)]
    string FirmwareVersion,

    [Required]
    string Generation,

    [Range(0, int.MaxValue)] int Configuration,
    [Range(0, int.MaxValue)] int Size,
    [Range(0, 16)] int MagnetometerCount,
    [Range(0, 16)] int AccelerometerCount,

    [MaxLength(1000)] string? Notes = null,

    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency.")]
    string? RowVersion = null);
