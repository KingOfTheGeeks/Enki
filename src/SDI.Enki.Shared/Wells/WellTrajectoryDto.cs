namespace SDI.Enki.Shared.Wells;

/// <summary>
/// Aggregate trajectory projection for the multi-well plot page.
/// One row per well under the requested Job; <see cref="Points"/>
/// is the well's tie-on (when present, as the depth-0 anchor) plus
/// every survey station, ordered by measured depth.
///
/// <para>
/// Storage convention: every coordinate (Md / Tvd / Northing /
/// Easting) is canonical SI metres, matching the rest of the API.
/// The Blazor plot pages project to ft / m / etc. at render time
/// via the units cascade.
/// </para>
///
/// <para>
/// The math behind <see cref="TrajectoryPointDto.Northing"/> /
/// <see cref="TrajectoryPointDto.Easting"/> /
/// <see cref="TrajectoryPointDto.Tvd"/> is owned by Marduk's
/// <c>ISurveyCalculator</c>; this DTO just carries the cached
/// computed values out of the database. Empty <see cref="Points"/>
/// is legal — a brand-new well that hasn't been surveyed yet still
/// shows up in the list, just with no curve to draw.
/// </para>
/// </summary>
public sealed record WellTrajectoryDto(
    int    Id,
    string Name,
    string Type,
    IReadOnlyList<TrajectoryPointDto> Points);

/// <summary>
/// One point on a well's trajectory polyline. Stations are joined
/// straight-line in 3-D for the plot — visually the wellpath looks
/// continuous because typical surveys are spaced tightly enough
/// that segment-projection error is invisible at chart resolution.
///
/// <para>
/// <see cref="VerticalSection"/> is the per-station projection of
/// (Northing, Easting) onto the well's tie-on
/// <c>VerticalSectionDirection</c>, computed by Marduk's auto-recalc
/// at save time and cached on the Survey row. Different wells under
/// the same Job may have different VSDs, so the multi-well vertical-
/// section view's X axis is per-well — comparable for wells that
/// share a VSD, divergent otherwise. Single-well view sidesteps the
/// ambiguity entirely.
/// </para>
/// </summary>
/// <param name="Md">Measured depth at the station, SI metres.</param>
/// <param name="Northing">Local-frame Northing, SI metres.</param>
/// <param name="Easting">Local-frame Easting, SI metres.</param>
/// <param name="Tvd">True vertical depth, SI metres (positive downward).</param>
/// <param name="VerticalSection">
/// Projected horizontal distance along the well's tie-on
/// <c>VerticalSectionDirection</c>, SI metres. Zero at the tie-on
/// (the projection origin) by definition.
/// </param>
public sealed record TrajectoryPointDto(
    double Md,
    double Northing,
    double Easting,
    double Tvd,
    double VerticalSection);
