using SDI.Enki.Core.Units;

namespace SDI.Enki.Infrastructure.Provisioning.Models;

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
    double SurfaceEasting);
