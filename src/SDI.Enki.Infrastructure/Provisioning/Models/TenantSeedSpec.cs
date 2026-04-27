using SDI.Enki.Core.Units;

namespace SDI.Enki.Infrastructure.Provisioning.Models;

/// <summary>
/// Shape of the tenant's primary seeded Job. Each value picks a
/// completely different trajectory generator inside
/// <see cref="DevTenantSeeder"/>; the enum exists so a tenant can
/// opt into the geometry that best demonstrates a particular
/// drilling-domain story without having to layer flag-on-flag.
///
/// <para>
/// Add-on Jobs (Macondo relief, Wytch Farm ERD) ride alongside the
/// primary Job via separate <c>Include…</c> flags on
/// <see cref="TenantSeedSpec"/>. The shape selected here determines
/// only the FIRST Job under the tenant.
/// </para>
/// </summary>
public enum MainJobShape
{
    /// <summary>
    /// Three-well parallel-lateral pilot — Target / Injection / Offset
    /// horizontal producer + injector pair plus a legacy vertical
    /// offset. ISCWSA-style. The original demo shape used by every
    /// tenant before the seed was diversified.
    /// </summary>
    StandardParallelLaterals,

    /// <summary>
    /// Eight-well unconventional pad — Permian Wolfcamp / Bakken-style.
    /// Eight surface holes within ~10 m of each other on a single pad,
    /// then they fan out, kick off below surface casing, and drill
    /// laterals to eight reservoir cells. Stress-tests anti-collision
    /// in the shallow section where all holes are close.
    /// </summary>
    MultiWellPad,

    /// <summary>
    /// SAGD producer + injector pair — Athabasca / Cold Lake-style.
    /// Two horizontal wells from a single pad, separated vertically
    /// by ~5 m over ~700 m of lateral. Demonstrates the "track a
    /// setpoint" use case for the anti-collision math (distinct
    /// from "stay away" and "converge to zero"). The classic
    /// SDI-MagTraC-ranging scenario.
    /// </summary>
    SagdPair,
}

/// <summary>
/// Per-tenant differentiation for <see cref="DevTenantSeeder"/>. The
/// trajectory math, tubular sizes, formation tops, and common-measure
/// signal factors stay constant across every demo tenant — only the
/// labels and surface coordinates change. That keeps the dev seed
/// realistic-looking (regional field names and grid positions) without
/// inventing three independent sets of physically-defensible survey
/// data.
///
/// <para>
/// Four specs ship today, deliberately split 2 × 2 across the
/// operational unit systems so the units display layer is exercised
/// on every login: Permian Crest Energy (PERMIAN — bootstrap, Field),
/// Bakken Ridge Petroleum (BAKKEN — Williston Basin, Field), Brent
/// Atlantic Drilling (NORTHSEA — UKCS, Metric), and Carnarvon
/// Offshore Pty (CARNARVON — NW Shelf, Metric). They differ in:
/// <list type="bullet">
///   <item>Tenant code / display label</item>
///   <item>Job name + region + unit-system preference</item>
///   <item>Lead-well name (carried on Job.WellName for the at-a-glance
///   header)</item>
///   <item>Target / Injector / Offset well names</item>
///   <item>Surface Northing / Easting — sited near each basin's real
///   grid coordinates so the absolute positions visibly differ between
///   tenants</item>
/// </list>
/// </para>
/// </summary>
/// <param name="MagneticDeclination">
/// Per-region geomagnetic declination, signed degrees. Positive →
/// magnetic north is east of true / grid north. Stored on every
/// well's per-well Magnetics row so the WellDetail page shows the
/// same approximate WMM-2026 values the field crew would have
/// actually applied.
/// </param>
/// <param name="MagneticDip">
/// Per-region geomagnetic dip / inclination, signed degrees.
/// Positive → field points downward (Northern hemisphere);
/// negative in the Southern hemisphere (CARNARVON).
/// </param>
/// <param name="MagneticTotalField">
/// Per-region total field strength, nanotesla — to match the
/// existing Magnetics column convention. Surface values run
/// 25–65 µT (= 25,000–65,000 nT).
/// </param>
public sealed record TenantSeedSpec(
    string Code,
    string Name,
    string DisplayName,
    string Notes,
    string JobName,
    string JobDescription,
    string Region,
    UnitSystem UnitSystem,
    string TargetWellName,
    string InjectorWellName,
    string OffsetWellName,
    double SurfaceNorthing,
    double SurfaceEasting,
    double MagneticDeclination,
    double MagneticDip,
    double MagneticTotalField)
{
    /// <summary>
    /// Geometry of the tenant's primary seeded Job. Defaults to
    /// <see cref="MainJobShape.StandardParallelLaterals"/> so existing
    /// callers keep their original 3-well-pilot shape; PERMIAN /
    /// BOREAL override to <see cref="MainJobShape.MultiWellPad"/> /
    /// <see cref="MainJobShape.SagdPair"/> respectively.
    /// </summary>
    public MainJobShape MainJobShape { get; init; } = MainJobShape.StandardParallelLaterals;

    /// <summary>
    /// Opt-in: also seed a second Job under this tenant containing
    /// the Macondo-style relief-well intercept demo — a runaway
    /// Target plus two Injection (relief) wells converging on it
    /// from offset surface sites via S-shape vertical / build /
    /// hold / drop / low-angle-approach trajectories, plus a far
    /// Offset producer for visual contrast on the cylinder plot.
    ///
    /// <para>
    /// Anti-collision-in-reverse: the math in
    /// <c>AMR.Core.Uncertainty.AntiCollisionScanner</c> is identical,
    /// but instead of "stay away" the relief curves converge to
    /// near-zero distance at TD. Showcase scenario for the
    /// travelling-cylinder view we just shipped.
    /// </para>
    ///
    /// <para>
    /// Default <c>false</c> so existing demo specs keep their
    /// original single-Job shape; PERMIAN sets it true to
    /// demonstrate the relief geometry.
    /// </para>
    /// </summary>
    public bool IncludeMacondoReliefJob { get; init; }

    /// <summary>
    /// Opt-in: also seed a second Job under this tenant containing
    /// the Wytch Farm M-11 ERD demo — two parallel extended-reach
    /// wells drilled from a single onshore pad reaching ~10 km
    /// laterally under offshore reservoir. Visually striking on
    /// the plan-view chart (axes stretch to 10 km+); validates that
    /// the trajectory plotter handles the geometric extreme.
    ///
    /// <para>
    /// Default <c>false</c>; NORTHSEA sets it true so the UK-flavoured
    /// tenant carries an onshore ERD example alongside its offshore
    /// parallel-lateral pilot.
    /// </para>
    /// </summary>
    public bool IncludeWytchFarmErdJob { get; init; }
}
