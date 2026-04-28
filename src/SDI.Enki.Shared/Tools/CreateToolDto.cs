using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Tools;

/// <summary>
/// Body for <c>POST /tools</c>. Used when a new tool arrives in the shop
/// and needs to be registered against the master fleet. Generation is
/// optional — if omitted, the controller infers from FirmwareVersion +
/// Configuration + Size using the same heuristic the seeder uses.
/// </summary>
public sealed record CreateToolDto(
    [Required, Range(1, int.MaxValue, ErrorMessage = "Serial must be a positive integer.")]
    int SerialNumber,

    [Required, MaxLength(64)]
    string FirmwareVersion,

    [Range(0, int.MaxValue)] int Configuration = 0,
    [Range(0, int.MaxValue)] int Size = 0,
    [Range(0, 16)] int MagnetometerCount = 0,
    [Range(0, 16)] int AccelerometerCount = 0,

    /// <summary>Optional override; null → infer from firmware + config + size.</summary>
    string? Generation = null,

    [MaxLength(1000)] string? Notes = null);
