using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Shots;

/// <summary>
/// Inputs for creating a Shot under a Run. Slim by design:
/// <list type="bullet">
///   <item>Identity only — <c>ShotName</c> + <c>FileTime</c>.</item>
///   <item>Calibration is <b>not</b> per-shot. It comes from the
///   parent Run's selected Tool; setting it here was a legacy
///   leftover.</item>
///   <item>Processing config is <b>not</b> free-form JSON the user
///   types. It's a typed Marduk class
///   (<c>AMR.Core.Gradient.Models.GradientConfiguration</c> or
///   <c>AMR.Core.Rotary.Models.RotatingDipoleConfiguration</c>) the
///   server populates from the run's tool/calibration. The
///   Shot.ConfigJson column persists the typed config as JSON, but
///   the UI never asks the user to author it.</item>
/// </list>
/// </summary>
public sealed record CreateShotDto(
    [Required(ErrorMessage = "Shot name is required.")]
    [MaxLength(200, ErrorMessage = "Shot name must be 200 characters or fewer.")]
    string ShotName,

    [Required(ErrorMessage = "File time is required.")]
    DateTimeOffset FileTime);
