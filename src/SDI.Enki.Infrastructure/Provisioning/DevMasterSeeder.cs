using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Units;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning.Models;
using SDI.Enki.Shared.Seeding;

namespace SDI.Enki.Infrastructure.Provisioning;

/// <summary>
/// Dev-only idempotent auto-provision for the curated set of demo
/// tenants. Runs at host startup when
/// <see cref="ProvisioningOptions.SeedSampleData"/> is on; skips
/// individual tenants that already exist (so adding a new one to the
/// roster doesn't re-provision the others).
///
/// <para>
/// Pairs with <see cref="DevTenantSeeder"/>: this side creates the
/// tenant registry rows + provisions the two databases, that side
/// fills the Active DB with demo Jobs / Wells / surveys based on the
/// per-tenant <see cref="TenantSeedSpec"/>. Together they mean a
/// fresh dev machine boots into a working multi-tenant state — four
/// tenants visible in the UI — without any manual clicks.
/// </para>
///
/// <para>
/// Three demo tenants ship today, each carrying a different
/// drilling-domain showcase so the cylinder view (and the rest of
/// the Wells surface) exercises distinct geometry classes:
/// </para>
/// <list type="bullet">
///   <item><c>PERMIAN</c> — Permian Crest Energy (Field, West Texas).
///     Primary Job: <b>8-well unconventional pad</b>
///     (<c>Crest-North-Pad</c>) — Wolfcamp-style fan of laterals,
///     real anti-collision pressure in the shallow section. Secondary
///     Job: <b>Macondo-style relief-well intercept</b> (<c>MC252-Relief</c>)
///     — anti-collision-in-reverse showcase.</item>
///   <item><c>NORTHSEA</c> — Brent Atlantic Drilling (Metric, UKCS).
///     Primary Job: parallel-lateral pilot (<c>Atlantic-26-7H</c>) —
///     classic 3-well producer + injector + offset trio, ISCWSA-style.
///     Secondary Job: <b>Wytch Farm M-series ERD demo</b>
///     (<c>Wytch-Farm-M-Series</c>) — ~10.7 km extended-reach laterals
///     from a single onshore pad, the geometric extreme.</item>
///   <item><c>BOREAL</c> — Boreal Athabasca Energy (Metric, NE Alberta).
///     Primary Job: <b>SAGD producer + injector pair</b>
///     (<c>Cold-Lake-Pad-7</c>) — 5 m vertical separation over ~700 m
///     of lateral, the canonical SDI MagTraC ranging scenario. Demonstrates
///     the "track a setpoint" use case for the anti-collision math
///     (distinct from "stay away" and "converge to zero").</item>
/// </list>
///
/// <para>
/// Earlier rosters carried BAKKEN + CARNARVON alongside the two
/// remaining demo tenants; they were trimmed once the seed
/// diversified into the showcase shapes above. BOREAL was reinstated
/// to host the Athabasca SAGD demo — Cold Lake operations are the
/// flagship SDI MagTraC ranging-tool use case, so giving it a tenant
/// of its own makes the demo's lineage obvious.
/// </para>
///
/// <para>
/// Strict SI is intentionally NOT in the seed: no live rig speaks it
/// (mud weight in pascals, depth in meters with N3 precision) — Field
/// + Metric covers every operational scenario, and the SI preset
/// stays available for any tenant that explicitly opts in.
/// </para>
///
/// <para>
/// Each tenant's Jobs are produced by separate seeders inside
/// <see cref="DevTenantSeeder"/>; the spec's
/// <see cref="TenantSeedSpec.MainJobShape"/> +
/// <see cref="TenantSeedSpec.IncludeMacondoReliefJob"/> +
/// <see cref="TenantSeedSpec.IncludeWytchFarmErdJob"/> flags select
/// which seeders run. See those seeders for per-shape geometry.
/// </para>
///
/// <para>
/// Failures are logged and swallowed per tenant — one tenant failing
/// to provision (e.g. its DB already exists in a half-migrated state)
/// doesn't block the others, and never crashes the host. Users can
/// recover via the provisioning UI.
/// </para>
/// </summary>
public static class DevMasterSeeder
{
    /// <summary>
    /// Canonical bootstrap tenant code — the first one a fresh boot
    /// provisions and the one test fixtures default to. Switching this
    /// to another roster entry doesn't reorder the seed; it only
    /// changes which code the supporting harness assumes is present.
    /// </summary>
    public const string BootstrapTenantCode = "PERMIAN";

    /// <summary>
    /// Curated demo-tenant roster. Three demo tenants ship today,
    /// each carrying a different drilling-domain showcase so the
    /// cylinder view + the rest of the Wells surface exercise
    /// distinct geometry classes:
    /// <list type="bullet">
    ///   <item><c>PERMIAN</c> — 8-well Wolfcamp pad + Macondo-style
    ///     relief-well intercept (Field).</item>
    ///   <item><c>NORTHSEA</c> — Brent parallel-lateral pilot +
    ///     Wytch Farm M-series ERD (Metric).</item>
    ///   <item><c>BOREAL</c> — SAGD producer + injector pair, the
    ///     canonical SDI MagTraC ranging scenario (Metric).</item>
    /// </list>
    ///
    /// <para>
    /// Earlier this roster was briefly trimmed to BOREAL-only after
    /// the multi-well-pad seeder produced NaN values that crashed
    /// SQL Server's float-column save. The root cause was an
    /// unclamped <c>dot(t1, t2)</c> in Marduk's
    /// <c>SurveyStationExtensions.AngleTo</c> — fixed in Marduk and
    /// pinned by a regression test
    /// (<c>Compute_NoNonFiniteOutput_OnConsecutiveIdenticalHorizontalTangents</c>).
    /// PERMIAN + NORTHSEA are restored now that the math is safe.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<TenantSeedSpec> DemoTenants =
    [
        // ---------- Field (US oilfield) ----------

        // Permian Basin, West Texas. Coords match the 1 500 000 ft /
        // 600 000 ft Texas state-plane baseline that the original
        // bootstrap seed used (kept stable so reset-dev scripts and
        // SQL spot-checks don't have to re-tune).
        //
        // Primary Job: 8-well unconventional pad. Secondary Job:
        // Macondo-style relief-well intercept, gated on
        // IncludeMacondoReliefJob.
        new TenantSeedSpec(
            Code:             BootstrapTenantCode,
            Name:             "Permian Crest Energy",
            DisplayName:      "Permian Crest",
            Notes:            "Auto-seeded by DevMasterSeeder. Permian Basin unconventional operator + Gulf-of-Mexico exploration arm; primary Job is an 8-well Wolfcamp pad, secondary is the Macondo-style relief-well demo.",
            JobName:          "Crest-North-Pad",
            JobDescription:   "Seed job — 8-well Wolfcamp pad. All eight surface holes within ~10 m on a single pad, then fanning to different reservoir cells across two stacked benches.",
            Region:           "Permian Basin — Wolfcamp",
            UnitSystem:       UnitSystem.Field,
            // TargetWellName / InjectorWellName / OffsetWellName are
            // unused for MultiWellPad (the pad seeder owns its own
            // well names) but kept here as plausible fallbacks for
            // any code path that reads them.
            TargetWellName:   "Crest North 1H",
            InjectorWellName: "Crest North 5H",
            OffsetWellName:   "Crest North 8H",
            SurfaceNorthing:      457_200,    // 1 500 000 ft Texas state plane
            SurfaceEasting:       182_880,    //   600 000 ft
            // Permian Basin (~31°N 102°W) — WMM-2026 approximate.
            MagneticDeclination:    5.0,      // east of true
            MagneticDip:           63.0,
            MagneticTotalField: 50_300)      // nT
        {
            MainJobShape            = MainJobShape.MultiWellPad,
            // Macondo-style relief-well demo as a second Job.
            IncludeMacondoReliefJob = true,
        },

        // ---------- Metric (international oilfield) ----------

        // North Sea / UKCS, Brent field redevelopment. Coords near
        // the Brent field (UTM 31N order of magnitude). Metric
        // (m / bar / °C / kg/m³) matches the operational convention
        // offshore UK.
        //
        // Primary Job: standard parallel-lateral pilot (the original
        // 3-well demo). Secondary Job: Wytch Farm M-series ERD,
        // gated on IncludeWytchFarmErdJob — same UK operator
        // umbrella, plausible "Brent Atlantic also runs the onshore
        // Dorset asset" flavour.
        new TenantSeedSpec(
            Code:             "NORTHSEA",
            Name:             "Brent Atlantic Drilling",
            DisplayName:      "Brent Atlantic",
            Notes:            "Auto-seeded by DevMasterSeeder. UK operator with offshore (Brent) + onshore (Wytch Farm) assets; primary Job is the Brent parallel-lateral pilot, secondary is the Wytch Farm M-series ERD demo.",
            JobName:          "Atlantic-26-7H",
            JobDescription:   "Seed job — Brent field horizontal redevelopment, ~3050 m MD.",
            Region:           "North Sea — UKCS",
            UnitSystem:       UnitSystem.Metric,
            TargetWellName:   "Brent A-12",
            InjectorWellName: "Brent A-13",
            OffsetWellName:   "Brent A-7",
            SurfaceNorthing:    6_700_000,
            SurfaceEasting:       460_000,
            // North Sea / UKCS Brent field (~61°N 1°E) — WMM-2026.
            MagneticDeclination:    0.5,      // near zero on UKCS
            MagneticDip:           73.0,
            MagneticTotalField: 50_500)      // nT
        {
            MainJobShape           = MainJobShape.StandardParallelLaterals,
            IncludeWytchFarmErdJob = true,
        },

        // Athabasca / Cold Lake, NE Alberta. Coords UTM 12N around
        // 54.5°N 110°W (Cold Lake area). The flagship SDI MagTraC
        // ranging-tool scenario — SAGD producer + injector pair
        // drilling. Metric.
        new TenantSeedSpec(
            Code:             "BOREAL",
            Name:             "Boreal Athabasca Energy",
            DisplayName:      "Boreal",
            Notes:            "Auto-seeded by DevMasterSeeder. Athabasca / Cold Lake bitumen operator. SAGD pair-drilling demo — the canonical SDI MagTraC ranging scenario (5 m setpoint over ~700 m of lateral).",
            JobName:          "Cold-Lake-Pad-7",
            JobDescription:   "Seed job — SAGD producer + injector pair, McMurray Formation, ~470 m TVD pay zone, ~700 m horizontal section.",
            Region:           "Athabasca — Cold Lake",
            UnitSystem:       UnitSystem.Metric,
            // Spec well names unused for SagdPair (the SAGD seeder
            // owns its own naming) but kept consistent with the
            // canonical Cold Lake naming pattern.
            TargetWellName:   "Cold Lake Pad-7 P1",
            InjectorWellName: "Cold Lake Pad-7 I1",
            OffsetWellName:   "Cold Lake Pad-7 V-3",
            SurfaceNorthing:    6_043_000,    // UTM 12N — Cold Lake area
            SurfaceEasting:       370_000,
            // NE Alberta (~54.5°N 110°W) — WMM-2026 approximate.
            MagneticDeclination:   14.0,      // strong east declination at high latitude
            MagneticDip:           78.0,
            MagneticTotalField: 57_500)      // nT
        {
            MainJobShape = MainJobShape.SagdPair,
        },
    ];

    public static async Task SeedAsync(
        IServiceProvider services,
        bool force = false,
        CancellationToken ct = default)
    {
        // Outermost try/catch: under no circumstances does a dev-seed
        // failure crash the WebApi host. Every downstream call (master
        // DB reachability check, schema query, provisioning) is wrapped.
        // Most likely failure modes are SQL Server unreachable or master
        // DB schema not yet migrated; users recover via the provisioning
        // UI or by fixing the environment.
        ILogger? logger = null;
        try
        {
            await using var scope = services.CreateAsyncScope();
            var sp      = scope.ServiceProvider;
            var options = sp.GetRequiredService<ProvisioningOptions>();
            logger      = sp.GetRequiredService<ILoggerFactory>()
                             .CreateLogger(typeof(DevMasterSeeder).FullName!);

            // The DI-level SeedSampleData flag is the WebApi-host gate
            // (SeedSampleData=true only in Development). The Migrator's
            // seed-demo-tenants command passes force=true to bypass it,
            // since it's an explicit operator action — distinct from the
            // implicit host-startup auto-provision. Either path leads to
            // the same demo-tenant set; force just changes which gate is
            // the source of truth.
            if (!force && !options.SeedSampleData)
            {
                logger.LogDebug("DevMasterSeeder skipped — SeedSampleData is off.");
                return;
            }

            var master       = sp.GetRequiredService<EnkiMasterDbContext>();
            var provisioning = sp.GetRequiredService<ITenantProvisioningService>();

            // Provision each demo tenant if it isn't already there.
            // Per-tenant try/catch so one failing (e.g. half-migrated
            // leftover DB from a previous interrupted boot) doesn't
            // skip the others.
            foreach (var spec in DemoTenants)
            {
                var exists = await master.Tenants
                    .AsNoTracking()
                    .AnyAsync(t => t.Code == spec.Code, ct);

                if (exists)
                {
                    logger.LogDebug(
                        "DevMasterSeeder skipped — tenant {Code} already exists.", spec.Code);
                    continue;
                }

                try
                {
                    // Pin the Tenant.Id from SeedTenants so seeded Tenant
                    // users (SeedUsers entries with UserType=Tenant) can
                    // bind to the same GUID. Provisioning UI / Migrator
                    // CLI paths leave this null and get a fresh ID.
                    var result = await provisioning.ProvisionAsync(
                        new ProvisionTenantRequest(
                            Code:           spec.Code,
                            Name:           spec.Name,
                            DisplayName:    spec.DisplayName,
                            ContactEmail:   null,
                            Notes:          spec.Notes,
                            SeedSampleData: true,
                            SeedSpec:       spec,
                            TenantId:       SeedTenants.IdForCode(spec.Code)),
                        ct);

                    logger.LogInformation(
                        "DevMasterSeeder provisioned {Code} ({TenantId}) — Active={Active}, Archive={Archive}, Schema={Schema}",
                        result.Code, result.TenantId,
                        result.ActiveDatabaseName, result.ArchiveDatabaseName, result.AppliedSchemaVersion);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "DevMasterSeeder failed to provision {Code}. Continuing with the remaining demo tenants.",
                        spec.Code);
                }
            }

            // Tenant memberships are seeded after the provisioning loop
            // so the assignments survive a partial-tenant boot (e.g.,
            // BOREAL provisioning failed → PERMIAN + NORTHSEA still get
            // their members). Idempotent — only inserts rows that don't
            // already exist, so flipping a user out of the seed deletes
            // nothing in already-running envs.
            try
            {
                await SeedTenantMembershipsAsync(master, logger, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "DevMasterSeeder: tenant-membership seeding failed; tenants are usable but " +
                    "non-admin Team users may not be able to reach them until memberships are " +
                    "added via the UI.");
            }
        }
        catch (Exception ex)
        {
            // Logger may not have been resolved yet (e.g., ServiceProvider
            // itself was broken). Fall back to Console so the failure is
            // still visible without dumping a stack trace into the host's
            // fatal-error path.
            if (logger is not null)
                logger.LogWarning(ex,
                    "DevMasterSeeder failed during outer setup. Host will continue; provision tenants manually via the UI if desired.");
            else
                Console.Error.WriteLine(
                    $"[DevMasterSeeder] swallowed startup error ({ex.GetType().Name}): {ex.Message}");
        }
    }

    /// <summary>
    /// Seed dev TenantUser membership rows so the role/subtype matrix
    /// can be exercised on a fresh boot without the admin manually
    /// adding members via the UI. Without these rows, non-admin Team
    /// users (Field/Office/Supervisor) authenticate fine but every
    /// tenant-scoped endpoint denies them — the demo can't show what
    /// Field-vs-Office-vs-Supervisor access looks like.
    ///
    /// <para>
    /// Picks SDI staff to memberships that hit each interesting case:
    ///   PERMIAN  — Dapo (Field), Douglas (Office), Joel (Office +
    ///              licensing capability), Jamie (Supervisor).
    ///   NORTHSEA — Travis (Field), James (Office), Jamie (Supervisor).
    ///   BOREAL   — Scott (Field), John (Field), Jamie (Supervisor).
    /// Adam, James-Powell, and the Tenant-type users are intentionally
    /// excluded so the demo always has a non-member Team example.
    /// </para>
    ///
    /// <para>
    /// Idempotent — only inserts the (TenantId, UserId) pairs missing
    /// from the table. Membership rows are NOT removed when a user
    /// drops out of this list, so a running env doesn't lose state on
    /// a seed-config edit.
    /// </para>
    /// </summary>
    private static async Task SeedTenantMembershipsAsync(
        EnkiMasterDbContext master,
        ILogger logger,
        CancellationToken ct)
    {
        // (TenantCode, MasterUserId) pairs. The TenantCode is resolved
        // to TenantId at insert time so an in-flight seed where the
        // tenant didn't provision skips that pair instead of failing.
        var assignments = new (string TenantCode, Guid UserId)[]
        {
            ("PERMIAN",  SeedUsers.DapoAjayi.MasterUserId),
            ("PERMIAN",  SeedUsers.DouglasRidgway.MasterUserId),
            ("PERMIAN",  SeedUsers.JoelHarrison.MasterUserId),
            ("PERMIAN",  SeedUsers.JamieDorey.MasterUserId),

            ("NORTHSEA", SeedUsers.TravisSolomon.MasterUserId),
            ("NORTHSEA", SeedUsers.JamesPowell.MasterUserId),
            ("NORTHSEA", SeedUsers.JamieDorey.MasterUserId),

            ("BOREAL",   SeedUsers.ScottBrandel.MasterUserId),
            ("BOREAL",   SeedUsers.JohnBorders.MasterUserId),
            ("BOREAL",   SeedUsers.JamieDorey.MasterUserId),
        };

        var tenantCodes = assignments.Select(a => a.TenantCode).Distinct().ToArray();
        var tenantIdByCode = await master.Tenants
            .AsNoTracking()
            .Where(t => tenantCodes.Contains(t.Code))
            .ToDictionaryAsync(t => t.Code, t => t.Id, ct);

        var added = 0;
        foreach (var (code, userId) in assignments)
        {
            if (!tenantIdByCode.TryGetValue(code, out var tenantId))
                continue;   // tenant not provisioned this boot — skip silently.

            var exists = await master.TenantUsers
                .AsNoTracking()
                .AnyAsync(tu => tu.TenantId == tenantId && tu.UserId == userId, ct);
            if (exists) continue;

            master.TenantUsers.Add(new TenantUser(tenantId, userId));
            added++;
        }

        if (added > 0)
        {
            await master.SaveChangesAsync(ct);
            logger.LogInformation(
                "DevMasterSeeder seeded {Count} TenantUser membership row(s).", added);
        }
        else
        {
            logger.LogDebug(
                "DevMasterSeeder: TenantUser memberships already in place; nothing to seed.");
        }
    }
}
