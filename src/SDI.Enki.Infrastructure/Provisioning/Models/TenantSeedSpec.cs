using SDI.Enki.Core.Units;

namespace SDI.Enki.Infrastructure.Provisioning.Models;

/// <summary>
/// Per-tenant differentiation for <see cref="DevTenantSeeder"/>. The
/// trajectory math, tubular sizes, formation tops, and mud-weight
/// profile stay constant across every demo tenant — only the
/// labels and surface coordinates change. That keeps the dev seed
/// realistic-looking (regional field names and grid positions) without
/// inventing three independent sets of physically-defensible survey
/// data.
///
/// <para>
/// Three specs ship today: Permian (TENANTTEST, the bootstrap demo),
/// Bakken (BAKKEN — Williston Basin), and North Sea (NORTHSEA —
/// offshore UK). They differ in:
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
