namespace SDI.Enki.Shared.Wells;

/// <summary>
/// Anti-collision scan of one target well against one offset well —
/// the per-target-station closest-approach distance and clock
/// position needed to render a travelling-cylinder strip. The
/// service-level call returns a list of these (one per offset under
/// the same Job); the rendering side draws one curve per offset.
///
/// <para>
/// Math owner: Marduk's <c>AMR.Core.Uncertainty.AntiCollisionScanner</c>.
/// Enki only ferries the input trajectories through the scanner and
/// projects the result onto the wire. No survey calculation happens
/// here — Survey + TieOn rows are read with their persisted
/// minimum-curvature outputs intact.
/// </para>
///
/// <para>
/// Storage convention: every length is canonical SI metres,
/// matching the rest of the trajectories API. The rendering side
/// projects to ft / m via the units cascade. Clock position is
/// degrees on the toolface clock — 0° = high side, 90° = right,
/// 180° = low side, 270° = left, clockwise as viewed looking down
/// the borehole. Bare degrees on the wire (no unit conversion);
/// the convention is universal across drilling software.
/// </para>
/// </summary>
/// <param name="OffsetWellId">
/// Database id of the offset well. Lets the rendering side wire
/// hover-clicks back through to the well-detail page.
/// </param>
/// <param name="OffsetWellName">
/// Display label for the chart legend (e.g. "Lambert 2I").
/// </param>
/// <param name="OffsetWellType">
/// Well-type smart-enum name (Target / Intercept / Offset). Drives
/// per-curve colour the same way <c>WellTrajectoryDto.Type</c> does
/// on the plan / vertical-section views.
/// </param>
/// <param name="Samples">
/// One <see cref="AntiCollisionSampleDto"/> per target station, in
/// measured-depth order. May be empty if the offset well had no
/// stations (the controller still emits an entry so the legend
/// stays stable across reloads).
/// </param>
public sealed record AntiCollisionScanDto(
    int    OffsetWellId,
    string OffsetWellName,
    string OffsetWellType,
    IReadOnlyList<AntiCollisionSampleDto> Samples);

/// <summary>
/// One closest-approach reading. <see cref="TargetMd"/> /
/// <see cref="TargetTvd"/> drive the vertical axis of a travelling-
/// cylinder plot (MD-along-hole or TVD-by-elevation, switchable),
/// <see cref="Distance"/> drives the radial axis, and
/// <see cref="ClockPositionDegrees"/> drives the colour or the
/// flat-strip horizontal axis when the cylinder is "unrolled".
///
/// <para>
/// v1 is deterministic — centre-to-centre 3-D distance, no
/// uncertainty cones. When the ISCWSA error model lands, this DTO
/// gains <c>MinProbable</c> / <c>MaxProbable</c> distance fields and
/// <see cref="Distance"/> stays as the nominal centre-line value;
/// the wire shape stays additive.
/// </para>
/// </summary>
/// <param name="TargetMd">
/// Measured depth on the target trajectory, SI metres.
/// </param>
/// <param name="TargetTvd">
/// True vertical depth at the target station, SI metres
/// (positive downward).
/// </param>
/// <param name="Distance">
/// 3-D centre-to-centre distance from the target station to the
/// closest point on the offset trajectory, SI metres.
/// </param>
/// <param name="ClockPositionDegrees">
/// Toolface clock-face angle, 0–360°. 0° = high side, 90° = right,
/// 180° = low side, 270° = left; clockwise as viewed down-hole.
/// For a perfectly vertical target the convention degenerates to a
/// compass bearing of the offset (0° = north, 90° = east).
/// </param>
public sealed record AntiCollisionSampleDto(
    double TargetMd,
    double TargetTvd,
    double Distance,
    double ClockPositionDegrees);
