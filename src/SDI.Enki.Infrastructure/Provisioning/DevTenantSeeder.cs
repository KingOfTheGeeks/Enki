using Microsoft.EntityFrameworkCore;
using SDI.Enki.Core.Master.Tools.Enums;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Jobs.Enums;
using SDI.Enki.Core.TenantDb.Logs;
using SDI.Enki.Core.TenantDb.Runs;
using SDI.Enki.Core.TenantDb.Runs.Enums;
using SDI.Enki.Core.TenantDb.Shots;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning.Models;
using SDI.Enki.Infrastructure.Surveys;
using TenantCalibration = SDI.Enki.Core.TenantDb.Shots.Calibration;

namespace SDI.Enki.Infrastructure.Provisioning;

/// <summary>
/// Seeds a freshly-provisioned tenant's Active DB with realistic demo
/// content — one Job, three Wells (one of each <see cref="WellType"/>),
/// each with its own tie-on, surveys, and (for the active producer) a
/// full casing / formation / common-measure stack. Three wells lets every
/// page on the Wells surface be exercised end-to-end against
/// representative data instead of toy values.
///
/// <para>
/// Trajectory shapes are modelled on ISCWSA's three published test
/// wells (the wellbore-survey-accuracy industry standard) and the
/// SAGD-style ranging case studies Schlumberger and Halliburton publish
/// from real Oman / Cold Lake projects:
/// </para>
///
/// <list type="bullet">
///   <item><b>Target</b> (per <c>spec.TargetWellName</c>): horizontal producer, ISCWSA Well 3 shape — vertical to ~914 m, build to 90°, hold lateral to 3048 m MD (10 000 ft in the source spec).</item>
///   <item><b>Injection</b> (per <c>spec.InjectorWellName</c>): adjacent parallel injection lateral ~15 m below the producer (CO2 / water-flood pattern). Same trajectory shape, slightly offset depths.</item>
///   <item><b>Offset</b> (per <c>spec.OffsetWellName</c>): older vertical neighbour for anti-collision, ISCWSA Well 1 shape — sparse stations, &lt;2° inclination drift.</item>
/// </list>
///
/// <para>
/// <b>Units convention:</b> every numeric value in this seeder is
/// stored in SI / metric — meters, kilograms-per-meter, kilograms-per-cubic-meter.
/// That's the database's storage convention (rule: "always metric in
/// the DB; convert at the GUI for display"). The Job is created with
/// <see cref="UnitSystem.Field"/> so the GUI display layer renders these
/// numbers back as feet / inches / lb-per-foot / ppg — the units a US
/// drilling engineer expects to see — but the bytes on disk are SI.
/// Source values from the ISCWSA spec are in feet / inches / lb-per-foot
/// and have been converted using exact factors:
/// 1 ft = 0.3048 m, 1 in = 0.0254 m, 1 lb/ft = 1.488164 kg/m.
/// (CommonMeasure values are stored as dimensionless multipliers and
/// don't need a unit conversion.)
/// </para>
///
/// <para>
/// Gated on <see cref="Models.ProvisioningOptions.SeedSampleData"/>:
/// WebApi turns it on in Development, Migrator CLI + prod hosts leave
/// it off. <see cref="DevMasterSeeder"/> is the only caller that sets
/// the per-request flag, so this content lands only inside the
/// curated demo roster (PERMIAN / BAKKEN / NORTHSEA / CARNARVON).
/// </para>
///
/// <para>
/// CreatedAt / CreatedBy are stamped by
/// <c>TenantDbContext.SaveChangesAsync</c> (it treats a default-valued
/// CreatedAt as unset). Don't hand-stamp them here — the auditor owns
/// those fields.
/// </para>
/// </summary>
public static class DevTenantSeeder
{
    public static async Task SeedAsync(
        TenantDbContext db,
        EnkiMasterDbContext master,
        ISurveyAutoCalculator autoCalculator,
        TenantSeedSpec spec,
        CancellationToken ct = default)
    {
        // Dispatch on the spec's MainJobShape to pick the primary-Job
        // geometry. Each shape owns its full Job + Wells + tie-ons +
        // surveys; spec params (TargetWellName / InjectorWellName /
        // OffsetWellName / SurfaceNorthing / SurfaceEasting / magnetics)
        // are interpreted by each shape as best fits — the standard
        // 3-well shape uses them literally, the multi-well-pad +
        // SAGD-pair shapes use surface coords + magnetics but pick
        // their own well names since they're not 3-well.
        switch (spec.MainJobShape)
        {
            case MainJobShape.StandardParallelLaterals:
                await SeedStandardParallelLateralJobAsync(db, autoCalculator, spec, ct);
                break;

            case MainJobShape.MultiWellPad:
                await SeedMultiWellPadJobAsync(db, autoCalculator, spec, ct);
                break;

            case MainJobShape.SagdPair:
                await SeedSagdPairJobAsync(db, autoCalculator, spec, ct);
                break;
        }

        // ---------- Optional add-on Jobs ----------
        // Each tenant can layer additional Jobs on top of its primary
        // shape via opt-in flags. Each add-on Job is self-contained
        // (own Job, own well names, own surface coords offset from
        // the primary).

        if (spec.IncludeMacondoReliefJob)
        {
            // PERMIAN sets this to land the Macondo-style relief
            // demo alongside its parallel-lateral / multi-well
            // pad — see SeedMacondoReliefJobAsync.
            await SeedMacondoReliefJobAsync(db, autoCalculator, spec, ct);
        }

        if (spec.IncludeWytchFarmErdJob)
        {
            // NORTHSEA sets this to land an onshore-pad ERD demo
            // (Wytch Farm M-series-style) alongside its offshore
            // parallel-lateral pilot — see SeedWytchFarmErdJobAsync.
            await SeedWytchFarmErdJobAsync(db, autoCalculator, spec, ct);
        }

        // ---------- Runs + Shots layer ----------
        // After every Job shape + add-on Job has populated wells +
        // surveys, layer on a randomised set of Runs and Shots so the
        // Runs / Shots / Logs UI surfaces have non-empty demo data on a
        // fresh -Reset. Bins come from Data/Seed/BinaryFiles/*.bin
        // (copied to the build output by the csproj None glob).
        await SeedRunsAndShotsAsync(db, master, spec, ct);
    }

    /// <summary>
    /// Layer randomised Runs + Shots on top of every Well that this
    /// tenant's seed has already created. Each Well gets 1–3 runs with
    /// random <see cref="RunType"/> mix; Gradient and Rotary runs each
    /// carry 5–25 Shots, each with a randomly-picked <c>.bin</c> from
    /// the build-output pool. Passive runs have no Shots — their
    /// capture lives directly on the Run row (PassiveBinary +
    /// PassiveConfigJson) — so a randomly-picked bin populates those
    /// columns instead.
    ///
    /// <para>
    /// <c>Random</c> is seeded deterministically from the spec's
    /// <see cref="TenantSeedSpec.JobName"/> via a fixed FNV-1a hash so
    /// each tenant gets its own consistent shape across <c>-Reset</c>
    /// cycles (stable screenshots, stable manual-test flows). The
    /// per-tenant differentiation gives the human tester variety
    /// across the demo roster without making the data churn between
    /// resets.
    /// </para>
    ///
    /// <para>
    /// Result columns (ResultJson / ResultStatus / GyroResult* /
    /// PassiveResult*) are deliberately left null. Marduk fires its
    /// shot-processing pipeline against rows where Binary is non-null
    /// and Result is null on the next compute trigger — so the seed
    /// stages "ready to compute" inputs and exercises the pipeline on
    /// first interaction rather than baking pre-computed results into
    /// disk.
    /// </para>
    ///
    /// <para>
    /// Quietly returns if the bin pool is empty (e.g. someone deleted
    /// the BinaryFiles folder). The other seeded entities still land;
    /// just no Runs / Shots — better than crashing the whole tenant
    /// provision over a missing seed asset.
    /// </para>
    /// </summary>
    private static async Task SeedRunsAndShotsAsync(
        TenantDbContext db,
        EnkiMasterDbContext master,
        TenantSeedSpec spec,
        CancellationToken ct)
    {
        var binPool = LoadBinPool();
        if (binPool.Length == 0) return;

        var rng = new Random(StableSeed(spec.JobName));

        // Every well the prior seed steps added. Project to a tiny
        // shape — we only need (WellId, JobId) to wire the Run FK
        // back to the right Job.
        var wells = await db.Wells
            .AsNoTracking()
            .Select(w => new { w.Id, w.JobId })
            .ToListAsync(ct);

        // Pull master tools + their latest non-superseded calibrations
        // once. Seeded Gradient/Rotary runs get a random tool from
        // this pool so shot creation works out of the box (the
        // ShotsController gates on Run.ToolId not being null). Empty
        // pool just leaves seeded runs tool-less; the human can assign
        // a tool from the UI later.
        var toolPool = await master.Tools
            .AsNoTracking()
            .Where(t => t.Status == ToolStatus.Active)
            .Select(t => new { t.Id, t.SerialNumber })
            .ToListAsync(ct);

        // Pre-cached map of (toolId → its latest non-superseded
        // master Calibration row) so the per-run snapshot loop is one
        // dictionary lookup. Tools without a non-superseded cal stay
        // out of the pool entirely (we can't snapshot what doesn't
        // exist).
        var latestCalByTool = await master.Calibrations
            .AsNoTracking()
            .Where(c => !c.IsSuperseded)
            .GroupBy(c => c.ToolId)
            .Select(g => g.OrderByDescending(c => c.CalibrationDate).First())
            .ToDictionaryAsync(c => c.ToolId, c => c, ct);

        var assignableTools = toolPool
            .Where(t => latestCalByTool.ContainsKey(t.Id))
            .ToArray();

        // Tenant-side snapshot row cache so multiple runs sharing the
        // same tool reuse the same tenant Calibration row (matches the
        // CalibrationSnapshotService.EnsureSnapshotAsync idempotence).
        var snapshotByMasterCalId = new Dictionary<Guid, TenantCalibration>();

        var now = DateTimeOffset.UtcNow;

        foreach (var well in wells)
        {
            var runCount = rng.Next(1, 4);   // 1, 2, or 3 runs per well
            for (var runIndex = 0; runIndex < runCount; runIndex++)
            {
                var type   = RunTypePool[rng.Next(RunTypePool.Length)];
                var status = RunStatusPool[rng.Next(RunStatusPool.Length)];

                // Run depths nest under a synthetic 0–N range. The
                // surveys themselves live on the Well; Run depths are
                // for the run's own metadata window (typically the
                // logged interval), not a Well constraint.
                var startDepth = rng.Next(0, 1500);
                var endDepth   = startDepth + rng.Next(200, 2000);

                // Per-run Magnetics row (required). Seed values come
                // from the spec's geomagnetic reference for the
                // tenant's region — same numbers the well-canonical
                // Magnetics carries, but a fresh per-run row so each
                // run can drift its own values independently if an
                // operator edits later.
                var magnetics = new Magnetics(
                    bTotal:      spec.MagneticTotalField,
                    dip:         spec.MagneticDip,
                    declination: spec.MagneticDeclination);

                var run = new Run(
                    name:        $"{type.Name} run {runIndex + 1}",
                    description: $"Seeded {type.Name.ToLowerInvariant()} run for visual stimulation.",
                    startDepth:  startDepth,
                    endDepth:    endDepth,
                    type:        type)
                {
                    JobId          = well.JobId,
                    Status         = status,
                    StartTimestamp = now.AddDays(-rng.Next(7, 60)),
                    EndTimestamp   = status == RunStatus.Completed
                        ? now.AddDays(-rng.Next(0, 6))
                        : (DateTimeOffset?)null,
                    Magnetics      = magnetics,    // EF wires MagneticsId on save
                };

                // Pick a tool + snapshot its latest cal (Gradient /
                // Rotary only — Passive runs don't process via the
                // calibration pipeline). Skips silently when the
                // master fleet is empty so the tenant still gets
                // tool-less seeded runs that the operator can assign
                // a tool to later.
                if (type != RunType.Passive && assignableTools.Length > 0)
                {
                    var tool = assignableTools[rng.Next(assignableTools.Length)];
                    var masterCal = latestCalByTool[tool.Id];

                    if (!snapshotByMasterCalId.TryGetValue(masterCal.Id, out var snapshot))
                    {
                        snapshot = new TenantCalibration
                        {
                            MasterCalibrationId = masterCal.Id,
                            ToolId              = masterCal.ToolId,
                            SerialNumber        = masterCal.SerialNumber,
                            CalibrationDate     = masterCal.CalibrationDate,
                            CalibratedBy        = masterCal.CalibratedBy,
                            PayloadJson         = masterCal.PayloadJson,
                            MagnetometerCount   = masterCal.MagnetometerCount,
                            IsNominal           = masterCal.IsNominal,
                        };
                        db.Calibrations.Add(snapshot);
                        snapshotByMasterCalId[masterCal.Id] = snapshot;
                    }

                    run.ToolId               = tool.Id;
                    run.SnapshotCalibration  = snapshot;
                }

                db.Runs.Add(run);
                await db.SaveChangesAsync(ct);   // need run.Id for shot FK

                if (type == RunType.Passive)
                {
                    // Passive: capture lives on the Run row, no Shots.
                    var bin = binPool[rng.Next(binPool.Length)];
                    run.PassiveBinary           = bin.Bytes;
                    run.PassiveBinaryName       = bin.Name;
                    run.PassiveBinaryUploadedAt = now;
                    run.PassiveConfigJson       = MakePlaceholderConfigJson(rng);
                    run.PassiveConfigUpdatedAt  = now;
                }
                else
                {
                    // Gradient + Rotary: 5–25 shots per run. Each
                    // shot pulls a random bin from the pool (with
                    // replacement — duplicates are fine for stimulus
                    // purposes).
                    var shotCount = rng.Next(5, 26);
                    for (var shotIndex = 1; shotIndex <= shotCount; shotIndex++)
                    {
                        var bin = binPool[rng.Next(binPool.Length)];
                        db.Shots.Add(new Shot
                        {
                            RunId            = run.Id,
                            ShotName         = $"shot-{shotIndex:D2}",
                            FileTime         = now.AddMinutes(-(shotCount - shotIndex) * 5),
                            Binary           = bin.Bytes,
                            BinaryName       = bin.Name,
                            BinaryUploadedAt = now,
                            ConfigJson       = MakePlaceholderConfigJson(rng),
                            ConfigUpdatedAt  = now,
                            // Default each seeded shot to the run's
                            // snapshot calibration (matches the
                            // controller's create-time default).
                            CalibrationId    = run.SnapshotCalibration?.Id,
                        });
                    }
                }

                // Logs are independent of Shots and any run type can
                // carry them. 0–3 per run gives the Logs grid varied
                // shapes (empty / sparse / fuller) across the demo
                // roster. Same bin pool + placeholder config as Shots.
                var logCount = rng.Next(0, 4);
                for (var logIndex = 1; logIndex <= logCount; logIndex++)
                {
                    var bin = binPool[rng.Next(binPool.Length)];
                    db.Logs.Add(new Log(
                        runId:    run.Id,
                        shotName: $"log-{logIndex:D2}",
                        fileTime: now.AddMinutes(-(logCount - logIndex) * 30))
                    {
                        Binary           = bin.Bytes,
                        BinaryName       = bin.Name,
                        BinaryUploadedAt = now,
                        ConfigJson       = MakePlaceholderConfigJson(rng),
                        ConfigUpdatedAt  = now,
                        CalibrationId    = run.SnapshotCalibration?.Id,
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Load every <c>*.bin</c> file under <c>Data/Seed/BinaryFiles/</c>
    /// in the build output into memory, once. Each bin is ~120 KB and
    /// there are 25 of them, so the total pool is &lt; 3 MB — fine to
    /// hold for the duration of a tenant provision. Returns an empty
    /// array if the directory is missing so the caller can no-op
    /// rather than crash.
    /// </summary>
    private static SeedBin[] LoadBinPool()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "BinaryFiles");
        if (!Directory.Exists(dir)) return [];

        return Directory.EnumerateFiles(dir, "*.bin")
            .Select(p => new SeedBin(Path.GetFileName(p), File.ReadAllBytes(p)))
            .ToArray();
    }

    /// <summary>
    /// Placeholder ConfigJson per shot / passive run. Real configs
    /// carry the downhole-toolchain processing parameters (filters,
    /// channel maps, envelope settings); this is a stand-in so the
    /// seeded rows have non-null Config + the calc pipeline can fire
    /// against them. Replace by editing this method (or pivoting to
    /// per-bin config files alongside <c>BinaryFiles/</c>) when the
    /// real config shape stabilises.
    /// </summary>
    private static string MakePlaceholderConfigJson(Random rng) =>
        $$"""{ "format": "v1", "placeholder": true, "seedNonce": {{rng.Next(1000, 9999)}} }""";

    /// <summary>
    /// Deterministic 32-bit hash of the tenant's JobName, used as the
    /// per-tenant <c>Random</c> seed. <c>string.GetHashCode</c> is
    /// randomised per-process in .NET Core; this FNV-1a-style rolling
    /// hash is stable so each tenant always reseeds to the same Run /
    /// Shot shape across <c>-Reset</c> cycles.
    /// </summary>
    private static int StableSeed(string s)
    {
        unchecked
        {
            var hash = (int)2166136261u;
            foreach (var c in s) hash = (hash ^ c) * 16777619;
            return hash;
        }
    }

    private static readonly RunType[] RunTypePool =
        [RunType.Gradient, RunType.Rotary, RunType.Passive];

    private static readonly RunStatus[] RunStatusPool =
        [RunStatus.Planned, RunStatus.Active, RunStatus.Completed, RunStatus.Suspended];

    private sealed record SeedBin(string Name, byte[] Bytes);

    /// <summary>
    /// Standard 3-well parallel-lateral pilot. Original demo Job
    /// shape; used by tenants that haven't opted into a different
    /// primary geometry. Target horizontal producer + Injection
    /// horizontal sibling ~15 m below + Offset legacy vertical
    /// neighbour. ISCWSA-style trajectories.
    /// </summary>
    private static async Task SeedStandardParallelLateralJobAsync(
        TenantDbContext db,
        ISurveyAutoCalculator autoCalculator,
        TenantSeedSpec spec,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // ---------- Job ----------
        // UnitSystem comes from the spec; data on disk stays SI either
        // way (rule: "always metric in the DB; convert at the GUI for
        // display"). The Job's UnitSystem just tells the future display
        // layer which units to render values in.
        var job = new Job(
            name:        spec.JobName,
            description: spec.JobDescription,
            unitSystem:  spec.UnitSystem)
        {
            Status         = JobStatus.Active,
            Region         = spec.Region,
            WellName       = spec.TargetWellName,
            StartTimestamp = now,
            EndTimestamp   = now.AddMonths(3),
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);   // need job.Id for the Well FKs

        // ---------- Wells ----------
        var target    = new Well(job.Id, spec.TargetWellName,    WellType.Target);
        var injector  = new Well(job.Id, spec.InjectorWellName,  WellType.Injection);
        var offset    = new Well(job.Id, spec.OffsetWellName,    WellType.Offset);
        db.Wells.AddRange(target, injector, offset);
        await db.SaveChangesAsync(ct);   // need well.Ids for child rows

        // Per-well helpers take the base surface coords from the spec
        // and apply the same relative offsets every tenant uses
        // (injector ~15m south, offset ~150m north / 100m east). That
        // preserves the visual geometry across tenants while letting
        // each one sit at its own basin's grid coordinates.
        SeedTargetWell  (db, target.Id,   spec.SurfaceNorthing, spec.SurfaceEasting);
        SeedInjectorWell(db, injector.Id, spec.SurfaceNorthing, spec.SurfaceEasting);
        SeedOffsetWell  (db, offset.Id,   spec.SurfaceNorthing, spec.SurfaceEasting);

        // Per-well magnetic reference. Every well in a Job sits in
        // the same basin and carries the same approximate WMM-2026
        // triple from the spec. Stored on the existing Magnetics
        // entity with a non-null WellId so the filtered unique
        // index treats them as per-well rows (distinct from the
        // legacy per-shot lookup pool).
        SeedWellMagnetics(db, target.Id,   spec);
        SeedWellMagnetics(db, injector.Id, spec);
        SeedWellMagnetics(db, offset.Id,   spec);

        await db.SaveChangesAsync(ct);

        // Recalculate trajectories so the very first GET /surveys after
        // seeding returns rows with TVD / DLS / North / East / etc.
        // already populated. Rule: the client never sees uncalculated
        // survey data — same guarantee the controllers enforce after
        // every Survey/TieOn mutation.
        await autoCalculator.RecalculateAsync(db, target.Id,   ct);
        await autoCalculator.RecalculateAsync(db, injector.Id, ct);
        await autoCalculator.RecalculateAsync(db, offset.Id,   ct);
    }

    /// <summary>
    /// Per-well magnetic reference. Wells under the same Job sit in
    /// the same basin and share the same approximate WMM-2026
    /// triple from the spec. Stored with a non-null
    /// <c>WellId</c> so the filtered unique index treats it as a
    /// per-well row, distinct from the legacy per-shot lookup pool
    /// (where <c>WellId IS NULL</c>).
    /// </summary>
    private static void SeedWellMagnetics(TenantDbContext db, int wellId, TenantSeedSpec spec)
    {
        db.Magnetics.Add(new Magnetics(
            bTotal:      spec.MagneticTotalField,
            dip:         spec.MagneticDip,
            declination: spec.MagneticDeclination)
        {
            WellId = wellId,
        });
    }

    /// <summary>
    /// Target well — the horizontal producer (named per
    /// <c>spec.TargetWellName</c>). Full survey set + casing stack +
    /// formations + signal-factor common measures. ISCWSA-Well-3 shape.
    /// </summary>
    private static void SeedTargetWell(TenantDbContext db, int wellId, double baseNorthing, double baseEasting)
    {
        // Surface tie-on at depth 0 — the conventional anchor at the
        // KB / rotary, with the first measured survey station starting
        // some way down the hole. Metric throughout (Northing /
        // Easting come from the spec — each tenant sites its wells
        // at its own basin's grid coordinates).
        db.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0)
        {
            Northing                 = baseNorthing,
            Easting                  = baseEasting,
            VerticalReference        = 0,
            SubSeaReference          = 0,
            VerticalSectionDirection = 180,
        });

        // Vertical → kick-off → horizontal, 10 stations at 304.8 m
        // intervals (1000-ft spacing in the source spec). Computed
        // columns (TVD / DLS / …) populate when the user clicks
        // Calculate; the seed leaves them at zero.
        var stations = new (double Depth, double Inc, double Az)[]
        {
            ( 304.8, 0.5, 180),    //  1 000 ft
            ( 609.6, 1.5, 180),    //  2 000 ft
            ( 914.4, 10.0, 180),   //  3 000 ft
            (1219.2, 35.0, 180),   //  4 000 ft
            (1524.0, 65.0, 180),   //  5 000 ft
            (1828.8, 88.0, 180),   //  6 000 ft
            (2133.6, 90.0, 180),   //  7 000 ft
            (2438.4, 90.0, 180),   //  8 000 ft
            (2743.2, 90.0, 180),   //  9 000 ft
            (3048.0, 90.0, 180),   // 10 000 ft
        };
        foreach (var s in stations)
            db.Surveys.Add(new Survey(wellId, s.Depth, s.Inc, s.Az));

        // Three-string design: surface → intermediate → production liner.
        // Diameters in m (source: industry-standard tubular ODs in inches),
        // weights in kg/m (source: lb/ft).
        db.Tubulars.AddRange(
            new Tubular(wellId, order: 0, type: TubularType.Casing,
                fromMeasured: 0, toMeasured: 609.6,         //   2 000 ft
                diameter: 0.339725, weight: 101.20)         // 13.375 in / 68 lb/ft
            { Name = "Surface casing" },
            new Tubular(wellId, order: 1, type: TubularType.Casing,
                fromMeasured: 0, toMeasured: 1524.0,        //   5 000 ft
                diameter: 0.244475, weight: 69.94)          //  9.625 in / 47 lb/ft
            { Name = "Intermediate casing" },
            new Tubular(wellId, order: 2, type: TubularType.Liner,
                fromMeasured: 1371.6, toMeasured: 3048.0,   // 4 500 / 10 000 ft
                diameter: 0.1397, weight: 25.30)            //  5.5 in / 17 lb/ft
            { Name = "Production liner" });

        // Three classic Permian / Texas tops along the vertical section.
        // Depths in m, measured along the wellbore (canonical depth on
        // a Formation; TVD is derived by Marduk from these MDs against
        // the surveys). Resistance in ohm-m (already SI).
        db.Formations.AddRange(
            new Formation(wellId, "Austin Chalk",
                fromMeasured: 762.0, toMeasured: 1066.8, resistance: 12)   //  2 500 / 3 500 ft
            { Description = "Carbonate, secondary target" },
            new Formation(wellId, "Buda",
                fromMeasured: 1066.8, toMeasured: 1371.6, resistance: 15)  //  3 500 / 4 500 ft
            { Description = "Limestone seal above Eagle Ford" },
            new Formation(wellId, "Eagle Ford",
                fromMeasured: 1371.6, toMeasured: 1676.4, resistance: 8)   //  4 500 / 5 500 ft
            { Description = "Source rock — primary target zone" });

        // Signal-calculation scaling factors by measured depth —
        // dimensionless multipliers (a "percentage of 1") that the
        // downhole signal processing applies per interval. Surface
        // section runs slightly attenuated (0.95), nominal through the
        // build, mild gain through the build-up section, and a small
        // high-side trim through the lateral. These are placeholder
        // demo values — real fudge factors come from instrument
        // calibration and formation response, not the seed.
        db.CommonMeasures.AddRange(
            new CommonMeasure(wellId, fromMeasured:    0.0, toMeasured:  609.6, value: 0.950),
            new CommonMeasure(wellId, fromMeasured:  609.6, toMeasured: 1371.6, value: 1.000),
            new CommonMeasure(wellId, fromMeasured: 1371.6, toMeasured: 2133.6, value: 1.050),
            new CommonMeasure(wellId, fromMeasured: 2133.6, toMeasured: 3048.0, value: 1.025));
    }

    /// <summary>
    /// Injector well — parallel injection lateral ~15 m below the
    /// producer (50 ft offset in the source spec; named per
    /// <c>spec.InjectorWellName</c>). Lighter footprint than the
    /// target: tie-on, survey set, two tubulars (injection wells use
    /// simpler completions). No formations / common-measures — those
    /// mirror the target's vertical section and are recorded once on
    /// the lead well in real-world programs.
    /// </summary>
    private static void SeedInjectorWell(TenantDbContext db, int wellId, double baseNorthing, double baseEasting)
    {
        // Tie-on at the surface (depth 0). Northing offset south of
        // the target's tie-on (~15 m) so the two laterals sit at
        // distinct grid coords. Same offset applied across every
        // tenant so the relative geometry of producer / injector
        // pairs reads identically in each demo.
        db.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0)
        {
            Northing                 = baseNorthing - 15.24,   // ~15 m south of target (50 ft)
            Easting                  = baseEasting,
            VerticalReference        = 0,
            SubSeaReference          = 0,
            VerticalSectionDirection = 180,
        });

        // Same vertical-to-horizontal shape, ~15 m depth offset (50 ft
        // in the source spec) so the lateral runs parallel to and
        // below the producer.
        var stations = new (double Depth, double Inc, double Az)[]
        {
            ( 304.80, 0.5, 180),    //  1 000 ft
            ( 609.60, 1.5, 180),    //  2 000 ft
            ( 929.64, 10.0, 180),   //  3 050 ft
            (1234.44, 35.0, 180),   //  4 050 ft
            (1539.24, 65.0, 180),   //  5 050 ft
            (1844.04, 88.0, 180),   //  6 050 ft
            (2148.84, 90.0, 180),   //  7 050 ft
            (2453.64, 90.0, 180),   //  8 050 ft
            (2758.44, 90.0, 180),   //  9 050 ft
            (3063.24, 90.0, 180),   // 10 050 ft
        };
        foreach (var s in stations)
            db.Surveys.Add(new Survey(wellId, s.Depth, s.Inc, s.Az));

        db.Tubulars.AddRange(
            new Tubular(wellId, order: 0, type: TubularType.Casing,
                fromMeasured: 0, toMeasured: 1539.24,        //  5 050 ft
                diameter: 0.244475, weight: 69.94)           //  9.625 in / 47 lb/ft
            { Name = "Injector casing" },
            new Tubular(wellId, order: 1, type: TubularType.Tubing,
                fromMeasured: 0, toMeasured: 3063.24,        // 10 050 ft
                diameter: 0.0889, weight: 13.84)             //  3.5 in / 9.3 lb/ft
            { Name = "Injection tubing" });
    }

    /// <summary>
    /// Offset well — older vertical neighbour (named per
    /// <c>spec.OffsetWellName</c>). Drilled circa 1985 in the source
    /// spec, sparse manual surveys, ~1° drift over ~2438 m (8000 ft).
    /// ISCWSA Well 1 shape. Used here as the anti-collision reference
    /// offset; the active program drills around it.
    /// </summary>
    private static void SeedOffsetWell(TenantDbContext db, int wellId, double baseNorthing, double baseEasting)
    {
        // Anti-collision reference offset, ~150 m north + ~120 m east
        // of the target — same offset across every tenant so the
        // anti-collision geometry reads consistently in each demo.
        db.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0)
        {
            Northing                 = baseNorthing + 152.4,   // ~150 m north of target (500 ft)
            Easting                  = baseEasting  + 121.92,  // ~120 m east of target (400 ft)
            VerticalReference        = 0,
            SubSeaReference          = 0,
            VerticalSectionDirection = 270,
        });

        // Sparse vertical survey set with mild westward drift, typical
        // of older drilling without rotary-steerable assemblies.
        var stations = new (double Depth, double Inc, double Az)[]
        {
            ( 304.8, 0.3, 268),    // 1 000 ft
            ( 914.4, 0.8, 265),    // 3 000 ft
            (1524.0, 1.2, 263),    // 5 000 ft
            (2133.6, 1.6, 262),    // 7 000 ft
            (2438.4, 1.8, 262),    // 8 000 ft
        };
        foreach (var s in stations)
            db.Surveys.Add(new Survey(wellId, s.Depth, s.Inc, s.Az));

        // One legacy casing string. Anti-collision references rarely
        // need full-fidelity completions data on the existing well —
        // depth + trajectory is what the planner cares about.
        db.Tubulars.Add(new Tubular(wellId, order: 0, type: TubularType.Casing,
            fromMeasured: 0, toMeasured: 2438.4,    // 8 000 ft
            diameter: 0.1778, weight: 38.69)        // 7.0 in / 26 lb/ft
        { Name = "Production casing (legacy)" });
    }

    // =================================================================
    // Macondo-style relief-well Job
    // =================================================================
    //
    // Anti-collision-in-reverse showcase for the travelling-cylinder
    // view. A near-vertical Target (the "runaway") sitting in deep
    // water; two Injection wells (relief-well primary + backup)
    // drilled from offset surface sites converging on it via
    // S-shape (vertical / build / hold / drop / low-angle approach)
    // trajectories; a far Offset producer staying constant in the
    // background as a contrast curve.
    //
    // On the cylinder plot, the relief curves descend from ~1.5 km
    // surface separation to <100 m at TD — the moneyshot that sells
    // the math. The offset producer's curve stays ~3 km flat
    // throughout, so the reliefs' converge-to-zero shape pops by
    // contrast.
    //
    // Storage convention is metric (rule: "always metric in the DB").
    // PERMIAN's UnitSystem (Field) flips the GUI to feet at render
    // time. Surface coords sit ~12 km offset from the parallel-
    // lateral Job's Permian Basin coords so SQL spot-checks can
    // tell the two Jobs' wells apart without consulting the JobId
    // column.

    private const double MacondoTargetTvd = 5_500.0;   // ~18 040 ft

    /// <summary>
    /// Lateral surface separation from the runaway Target to each
    /// relief well's surface site. Big enough to be safe from the
    /// blowout's surface plume, small enough that a 4 000 m relief
    /// trajectory can converge on the target column at depth.
    /// 1 500 m = ~4 920 ft.
    /// </summary>
    private const double MacondoReliefSurfaceOffset = 1_500.0;

    private static async Task SeedMacondoReliefJobAsync(
        TenantDbContext db,
        ISurveyAutoCalculator autoCalculator,
        TenantSeedSpec spec,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // The relief Job sits under the same tenant as the parallel-
        // lateral pilot, so it inherits the same UnitSystem (Field for
        // PERMIAN). The Job name + Region are deliberately Gulf-of-
        // Mexico flavoured to evoke the canonical Macondo response —
        // the operator's "exploration arm" is plausible flavour for a
        // Permian Basin-headquartered company.
        var job = new Job(
            name:        "MC252-Relief",
            description: "Seed job — Macondo-style relief-well intercept demo. Twin reliefs from offset sites converge on a near-vertical runaway via S-shape trajectories. Demos travelling-cylinder anti-collision in reverse.",
            unitSystem:  spec.UnitSystem)
        {
            Status         = JobStatus.Active,
            Region         = "Gulf of Mexico — Mississippi Canyon (exploration)",
            WellName       = "MC252 Macondo",
            StartTimestamp = now,
            EndTimestamp   = now.AddMonths(4),
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);

        // Surface site for the runaway. Coords are deliberately
        // ~12 km offset from PERMIAN's parallel-lateral Job so the
        // two Jobs' wells live at visibly distinct grid positions
        // when SQL-inspected. Absolute values are otherwise arbitrary
        // — the cylinder math reads relative geometry only.
        var baseN = spec.SurfaceNorthing + 12_000.0;
        var baseE = spec.SurfaceEasting  + 12_000.0;

        // ---------- Wells ----------
        var runaway   = new Well(job.Id, "MC252 Macondo",        WellType.Target);
        var reliefA   = new Well(job.Id, "Development Driller II",  WellType.Injection);
        var reliefB   = new Well(job.Id, "Development Driller III", WellType.Injection);
        var producer  = new Well(job.Id, "Atlantis-7 Producer",   WellType.Offset);
        db.Wells.AddRange(runaway, reliefA, reliefB, producer);
        await db.SaveChangesAsync(ct);

        SeedMacondoRunaway   (db, runaway.Id,   baseN, baseE);
        SeedMacondoRelief    (db, reliefA.Id,
            tieOnNorthing: baseN + MacondoReliefSurfaceOffset,    // surface 1500 m NORTH of runaway
            tieOnEasting:  baseE,
            approachAzimuth: 180.0);                              // approaches from north → drives south
        SeedMacondoRelief    (db, reliefB.Id,
            tieOnNorthing: baseN,
            tieOnEasting:  baseE + MacondoReliefSurfaceOffset,    // surface 1500 m EAST of runaway
            approachAzimuth: 270.0);                              // approaches from east → drives west
        SeedMacondoProducer  (db, producer.Id,
            tieOnNorthing: baseN,
            tieOnEasting:  baseE - 3_000.0);                      // 3 km west, never threatens convergence

        // Magnetic reference. Gulf of Mexico (~28°N 88°W) — WMM-2026
        // approximate values. Different from PERMIAN's onshore
        // values but stored the same way (per-well row, not the
        // legacy lookup pool). All four wells share the same triple
        // since they sit in the same basin.
        SeedReliefWellMagnetics(db, runaway.Id);
        SeedReliefWellMagnetics(db, reliefA.Id);
        SeedReliefWellMagnetics(db, reliefB.Id);
        SeedReliefWellMagnetics(db, producer.Id);

        await db.SaveChangesAsync(ct);

        // Marduk auto-recalc populates Northing / Easting / TVD /
        // DLS / V-sect on every survey + tie-on, so the very first
        // travelling-cylinder fetch returns a fully-computed
        // trajectory. Same guarantee every controller enforces
        // after a survey/tie-on mutation.
        await autoCalculator.RecalculateAsync(db, runaway.Id,  ct);
        await autoCalculator.RecalculateAsync(db, reliefA.Id,  ct);
        await autoCalculator.RecalculateAsync(db, reliefB.Id,  ct);
        await autoCalculator.RecalculateAsync(db, producer.Id, ct);
    }

    /// <summary>
    /// Gulf of Mexico magnetic reference (~28°N 88°W) — WMM-2026
    /// approximate values. All four wells in the relief Job share
    /// the same triple since they sit in the same basin.
    /// </summary>
    private static void SeedReliefWellMagnetics(TenantDbContext db, int wellId)
    {
        db.Magnetics.Add(new Magnetics(
            bTotal:      47_500,    // nT — typical Gulf of Mexico
            dip:         58.0,      // signed degrees, downward in N hemisphere
            declination: -1.0)      // signed degrees, slightly west of true
        {
            WellId = wellId,
        });
    }

    /// <summary>
    /// Runaway target — the well being killed. Near-vertical from
    /// surface to ~5 500 m TVD (~18 040 ft, deep-water exploration
    /// depth). Slight azimuth-180° drift at 0.3° inclination so the
    /// clock-position math has a defined orientation rather than the
    /// degenerate vertical-tangent case.
    /// </summary>
    private static void SeedMacondoRunaway(
        TenantDbContext db, int wellId, double tieOnNorthing, double tieOnEasting)
    {
        db.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0)
        {
            Northing                 = tieOnNorthing,
            Easting                  = tieOnEasting,
            VerticalReference        = 0,
            SubSeaReference          = 0,
            VerticalSectionDirection = 180,
        });

        // 10 stations every ~610 m (2 000 ft) — a near-vertical drop
        // with a faint southerly drift (matches what a real exploration
        // well surveys on a vertical hole, never mathematically perfect).
        var stations = new (double Depth, double Inc, double Az)[]
        {
            ( 609.6, 0.3, 180),    //  2 000 ft
            (1219.2, 0.3, 180),    //  4 000 ft
            (1828.8, 0.3, 180),    //  6 000 ft
            (2438.4, 0.3, 180),    //  8 000 ft
            (3048.0, 0.3, 180),    // 10 000 ft
            (3657.6, 0.3, 180),    // 12 000 ft
            (4267.2, 0.3, 180),    // 14 000 ft
            (4876.8, 0.3, 180),    // 16 000 ft
            (MacondoTargetTvd, 0.3, 180),  // ~18 040 ft (TD)
        };
        foreach (var s in stations)
            db.Surveys.Add(new Survey(wellId, s.Depth, s.Inc, s.Az));

        // Two-string deep-water exploration completion — surface
        // casing through the shallow hazards, deep production
        // casing to TD.
        db.Tubulars.AddRange(
            new Tubular(wellId, order: 0, type: TubularType.Casing,
                fromMeasured: 0, toMeasured: 1_524.0,           //  5 000 ft
                diameter: 0.339725, weight: 101.20)             // 13.375 in / 68 lb/ft
            { Name = "Surface casing" },
            new Tubular(wellId, order: 1, type: TubularType.Casing,
                fromMeasured: 0, toMeasured: MacondoTargetTvd,  // to TD
                diameter: 0.244475, weight: 69.94)              //  9.625 in / 47 lb/ft
            { Name = "Production casing" });
    }

    /// <summary>
    /// Relief well S-shape — vertical / build / hold-tangent /
    /// drop / low-angle approach. Surface tie-on at
    /// (<paramref name="tieOnNorthing"/>, <paramref name="tieOnEasting"/>);
    /// drills toward the runaway via <paramref name="approachAzimuth"/>
    /// (180° = drives south, 270° = drives west, etc.). Same
    /// trajectory math regardless of approach side — only the
    /// azimuth changes — so DDII (north surface, drives south) and
    /// DDIII (east surface, drives west) share this method.
    ///
    /// <para>
    /// Geometry sized so the relief reaches the runaway's vertical
    /// column at <see cref="MacondoTargetTvd"/> with closest
    /// approach &lt;100 m at TD. Total MD ~6 000 m (~19 700 ft).
    /// </para>
    ///
    /// <list type="bullet">
    ///   <item>0 → 2 200 m: vertical, drilling straight down off
    ///   the offset rig (the "couple thousand vertical" before
    ///   kick-off).</item>
    ///   <item>2 200 → 3 000 m: build phase, kick off + build to
    ///   55° inclination while turning to <paramref name="approachAzimuth"/>.</item>
    ///   <item>3 000 → 3 600 m: hold tangent at 55° — the workhorse
    ///   section that accumulates the lateral displacement toward
    ///   the runaway.</item>
    ///   <item>3 600 → 4 800 m: drop phase, drop inclination from
    ///   55° back to ~5° so the relief's tangent at intercept
    ///   approximately matches the runaway's vertical tangent
    ///   (low-angle intercept = ranging accuracy holds).</item>
    ///   <item>4 800 → 6 000 m: near-vertical approach; final
    ///   ~1 200 m drilled at 1–3° inclination on
    ///   <paramref name="approachAzimuth"/>, gradually closing the
    ///   last bit of lateral separation while running ~parallel
    ///   to the runaway.</item>
    /// </list>
    /// </summary>
    private static void SeedMacondoRelief(
        TenantDbContext db,
        int wellId,
        double tieOnNorthing,
        double tieOnEasting,
        double approachAzimuth)
    {
        // Vertical-section direction matches the approach azimuth so
        // the V-sect projection on the relief reads sensibly.
        db.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0)
        {
            Northing                 = tieOnNorthing,
            Easting                  = tieOnEasting,
            VerticalReference        = 0,
            SubSeaReference          = 0,
            VerticalSectionDirection = approachAzimuth,
        });

        // S-shape survey set — see XML doc for the phase breakdown.
        // ~30 stations, mixed spacing (200 m through curvy sections,
        // 400 m through tangent / vertical).
        var stations = new (double Depth, double Inc, double Az)[]
        {
            // ----- Vertical phase: 0 → 2 200 m -----
            (  300, 0.0, 0),                 // straight down — no defined azimuth yet
            (  600, 0.0, 0),
            ( 1000, 0.0, 0),
            ( 1400, 0.0, 0),
            ( 1800, 0.0, 0),
            ( 2200, 0.5, approachAzimuth),   // first hint of kick-off

            // ----- Build phase: 2 200 → 3 000 m, 0° → 55° at
            //       approachAzimuth -----
            ( 2400,  8.0, approachAzimuth),
            ( 2600, 20.0, approachAzimuth),
            ( 2800, 38.0, approachAzimuth),
            ( 3000, 55.0, approachAzimuth),

            // ----- Hold-tangent phase: 3 000 → 3 600 m at 55°.
            //       Workhorse section — the relief moves laterally
            //       toward the runaway here. -----
            ( 3200, 55.0, approachAzimuth),
            ( 3400, 55.0, approachAzimuth),
            ( 3600, 55.0, approachAzimuth),

            // ----- Drop phase: 3 600 → 4 800 m, 55° → ~5°.
            //       Bringing the tangent back to near-vertical so
            //       the relief's attitude matches the runaway's at
            //       intercept (low angle = ranging works). -----
            ( 3800, 50.0, approachAzimuth),
            ( 4000, 40.0, approachAzimuth),
            ( 4200, 30.0, approachAzimuth),
            ( 4400, 20.0, approachAzimuth),
            ( 4600, 12.0, approachAzimuth),
            ( 4800,  5.0, approachAzimuth),

            // ----- Low-angle approach: 4 800 → 6 000 m.
            //       Final ~1 200 m drilled near-vertical on the
            //       approach azimuth, closing the last bit of
            //       lateral separation. Closest approach falls
            //       inside this section. -----
            ( 5000,  3.0, approachAzimuth),
            ( 5200,  2.0, approachAzimuth),
            ( 5400,  1.0, approachAzimuth),
            ( 5600,  1.0, approachAzimuth),
            ( 5800,  1.0, approachAzimuth),
            ( 6000,  1.0, approachAzimuth),
        };
        foreach (var s in stations)
            db.Surveys.Add(new Survey(wellId, s.Depth, s.Inc, s.Az));

        // Lighter completion — relief wells aren't producers and
        // generally run a single intermediate string + an open hole
        // through the build/drop. One casing string is enough demo.
        db.Tubulars.Add(new Tubular(wellId, order: 0, type: TubularType.Casing,
            fromMeasured: 0, toMeasured: 3_000.0,        // through the build to start of hold
            diameter: 0.244475, weight: 69.94)           //  9.625 in / 47 lb/ft
        { Name = "Relief intermediate casing" });
    }

    /// <summary>
    /// Atlantis-7 producer — far-away vertical reference. Doesn't
    /// participate in the kill; its job on the cylinder plot is to
    /// provide a curve that stays ~3 km flat throughout, so the
    /// reliefs' converge-to-zero shape pops by contrast.
    /// </summary>
    private static void SeedMacondoProducer(
        TenantDbContext db, int wellId, double tieOnNorthing, double tieOnEasting)
    {
        db.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0)
        {
            Northing                 = tieOnNorthing,
            Easting                  = tieOnEasting,
            VerticalReference        = 0,
            SubSeaReference          = 0,
            VerticalSectionDirection = 90,
        });

        // Mostly vertical with a faint easterly drift — a typical
        // older deep-water producer drilled before rotary-steerable
        // tooling could hold a perfect vertical hole.
        var stations = new (double Depth, double Inc, double Az)[]
        {
            ( 1000, 0.4, 90),
            ( 2000, 0.7, 88),
            ( 3000, 1.0, 88),
            ( 4000, 1.2, 87),
            ( 5000, 1.4, 87),
            (MacondoTargetTvd, 1.5, 87),
        };
        foreach (var s in stations)
            db.Surveys.Add(new Survey(wellId, s.Depth, s.Inc, s.Az));

        db.Tubulars.Add(new Tubular(wellId, order: 0, type: TubularType.Casing,
            fromMeasured: 0, toMeasured: MacondoTargetTvd,
            diameter: 0.244475, weight: 69.94)           //  9.625 in / 47 lb/ft
        { Name = "Production casing (Atlantis-7)" });
    }

    // =================================================================
    // Multi-well unconventional pad
    // =================================================================
    //
    // Permian Wolfcamp / Bakken-style 8-well drilling pad. All eight
    // surface holes sit within ~10 m of each other on the pad; the
    // wells then drill straight down off the pad through the surface
    // hazards before kicking off below ~1 500 m TVD and turning to
    // their individual reservoir cells. Lateral azimuths fan over a
    // ~30° spread (the reservoir trend, not 360°) and landing depths
    // are stacked across two reservoir benches so the wells cover the
    // pay vertically as well as laterally.
    //
    // On the cylinder plot from any one well: 7 sibling curves, very
    // close to each other in the shallow vertical section (real
    // anti-collision pressure — wells separated by &lt;10 m for the
    // first ~1 500 m of MD), then diverging as the laterals fan out.
    // That pattern is what real drilling-engineer anti-collision
    // monitoring looks like — not a clean 1-on-1 case.

    private static async Task SeedMultiWellPadJobAsync(
        TenantDbContext db,
        ISurveyAutoCalculator autoCalculator,
        TenantSeedSpec spec,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var job = new Job(
            name:        spec.JobName,
            description: spec.JobDescription,
            unitSystem:  spec.UnitSystem)
        {
            Status         = JobStatus.Active,
            Region         = spec.Region,
            WellName       = "Crest North 1H",
            StartTimestamp = now,
            EndTimestamp   = now.AddMonths(6),
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);

        // Eight well configs — surface tightly clustered (≤10 m), each
        // heading roughly south at slightly different azimuths to
        // different reservoir cells. Stacked vertically across two
        // benches: 4 wells land at ~1 200 m TVD (Wolfcamp A), 4 wells
        // at ~1 400 m TVD (Wolfcamp B). Total MD per well ~3 600 m
        // (= ~12 000 ft).
        var configs = new (string Name, double NorthingOffset, double EastingOffset, double LateralAz, double LandingTvd)[]
        {
            ("Crest North 1H", -3.0, -3.0, 175.0, 1_200.0),
            ("Crest North 2H", -3.0,  0.0, 180.0, 1_200.0),
            ("Crest North 3H", -3.0,  3.0, 185.0, 1_200.0),
            ("Crest North 4H",  0.0, -3.0, 178.0, 1_400.0),
            ("Crest North 5H",  0.0,  0.0, 182.0, 1_400.0),
            ("Crest North 6H",  0.0,  3.0, 186.0, 1_400.0),
            ("Crest North 7H",  3.0,  0.0, 180.0, 1_300.0),
            ("Crest North 8H",  3.0,  3.0, 184.0, 1_300.0),
        };

        var wellIds = new int[configs.Length];

        for (var i = 0; i < configs.Length; i++)
        {
            var c = configs[i];
            // First well is the Target (the one being actively drilled
            // / monitored). Others are mostly Injection (waterflood
            // pattern wells) with one Offset (the legacy producer on
            // the pad that was drilled in a prior phase). The Target
            // / Injection / Offset distinction here is for chart
            // colour-coding rather than operational meaning — eight
            // siblings on a real pad are all of the same operational
            // class.
            var type = i == 0 ? WellType.Target
                     : i == 7 ? WellType.Offset
                     : WellType.Injection;

            var well = new Well(job.Id, c.Name, type);
            db.Wells.Add(well);
            await db.SaveChangesAsync(ct);
            wellIds[i] = well.Id;

            SeedMultiWellPadWell(db, well.Id,
                tieOnNorthing: spec.SurfaceNorthing + c.NorthingOffset,
                tieOnEasting:  spec.SurfaceEasting  + c.EastingOffset,
                lateralAz:     c.LateralAz,
                landingTvd:    c.LandingTvd);
            SeedWellMagnetics(db, well.Id, spec);
        }

        await db.SaveChangesAsync(ct);

        for (var i = 0; i < wellIds.Length; i++)
            await autoCalculator.RecalculateAsync(db, wellIds[i], ct);
    }

    /// <summary>
    /// One well of the 8-well pad. Vertical to ~1 000 m TVD, build to
    /// 90° between 1 000 m and the landing depth, then hold horizontal
    /// to total MD ~3 600 m. Landing depth varies per well to stack
    /// the pad across reservoir benches; lateral azimuth varies to
    /// fan the wells over the reservoir.
    /// </summary>
    private static void SeedMultiWellPadWell(
        TenantDbContext db,
        int wellId,
        double tieOnNorthing,
        double tieOnEasting,
        double lateralAz,
        double landingTvd)
    {
        db.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0)
        {
            Northing                 = tieOnNorthing,
            Easting                  = tieOnEasting,
            VerticalReference        = 0,
            SubSeaReference          = 0,
            VerticalSectionDirection = lateralAz,
        });

        // Build window sized so the well lands at 90° at the
        // requested landingTvd. Linear-inc-vs-MD build adds
        // (2/π) ≈ 0.6366 of the build-MD as TVD, so the build
        // window length = (landingTvd - kop) / 0.6366. KOP fixed
        // at 1 000 m for all wells; per-bench landing depths come
        // from the buildMd derivation below. Total MD = landing +
        // 2 km of lateral hold.
        //
        // Earlier version of this method hardcoded the build-window
        // length and parameterised landing as `landingTvd + 200`,
        // which produced **non-monotonic** depth sequences for some
        // landingTvd values (e.g. landingTvd=1200 yielded a station
        // sequence of ..., 1600, 1350, 1400, ...). Marduk's
        // auto-recalc rejects non-monotonic depths and leaves every
        // computed column at zero, which manifested as a single
        // diagonal line on the plan-view chart. Parameterising
        // build-MD by landingTvd guarantees monotonicity.
        const double kop = 1_000.0;
        var buildMd  = (landingTvd - kop) / 0.6366;             // ≈ 314 m for 1200 / 628 m for 1400
        var landing  = kop + buildMd;
        var totalMd  = landing + 2_000.0;

        var stations = new List<(double Depth, double Inc, double Az)>
        {
            (  300.0, 0.3, lateralAz),
            (  600.0, 0.3, lateralAz),
            (  900.0, 0.4, lateralAz),
            ( kop,    0.5, lateralAz),                          // KOP

            // Build: linear inc vs MD across (kop → landing).
            // Stations at 25/50/75% of buildMd give a smooth
            // 0° → 90° progression at any buildMd length.
            ( kop + buildMd * 0.25, 22.5, lateralAz),
            ( kop + buildMd * 0.50, 45.0, lateralAz),
            ( kop + buildMd * 0.75, 67.5, lateralAz),
            ( landing,              90.0, lateralAz),           // landing — fully horizontal

            // Lateral hold to TD.
            ( landing +  500, 90.0, lateralAz),
            ( landing + 1000, 90.0, lateralAz),
            ( landing + 1500, 90.0, lateralAz),
            ( totalMd,        90.0, lateralAz),
        };

        foreach (var s in stations)
            db.Surveys.Add(new Survey(wellId, s.Depth, s.Inc, s.Az));

        // Three-string pad-well completion: surface, intermediate,
        // production liner. Same shape as the standard target well
        // but sized for a deeper landing.
        db.Tubulars.AddRange(
            new Tubular(wellId, order: 0, type: TubularType.Casing,
                fromMeasured: 0, toMeasured: 600.0,
                diameter: 0.339725, weight: 101.20)              // 13.375 in / 68 lb/ft
            { Name = "Surface casing" },
            new Tubular(wellId, order: 1, type: TubularType.Casing,
                fromMeasured: 0, toMeasured: kop + 100,
                diameter: 0.244475, weight: 69.94)               //  9.625 in / 47 lb/ft
            { Name = "Intermediate casing" },
            new Tubular(wellId, order: 2, type: TubularType.Liner,
                fromMeasured: landing - 200, toMeasured: totalMd,
                diameter: 0.1397, weight: 25.30)                 //  5.5 in / 17 lb/ft
            { Name = "Production liner" });
    }

    // =================================================================
    // SAGD producer / injector pair
    // =================================================================
    //
    // Athabasca / Cold Lake-style Steam-Assisted Gravity Drainage:
    // a horizontal Producer at the bottom of the McMurray-Formation
    // pay zone + a horizontal Injector ~5 m directly above it, both
    // ~700 m laterals, drilled from the same surface pad. The
    // injector pumps steam down; oil heats up, viscosity drops, and
    // it falls into the producer. The 5 m vertical separation is
    // the whole game — too close = thermal short-circuit; too far =
    // no gravity drainage.
    //
    // Holding the pair to ±0.5 m of the 5 m setpoint over kilometres
    // of horizontal section is exactly what passive magnetic ranging
    // (SDI's MagTraC) is for. Cylinder plot from the producer with
    // the injector as offset shows distance ~5 m flat for the entire
    // ~700 m of pair section — a third use case for the same math
    // (after anti-collision "stay away" and relief "converge to
    // zero"): tracking a setpoint.
    //
    // Two wells only — producer + injector. An earlier version
    // also seeded a legacy CHOPS vertical producer on the pad as
    // an anti-collision reference, but on the cylinder plot its
    // distance grew from ~50 m at surface to ~870 m as the SAGD
    // pair drilled away east. That dominated the chart x-axis and
    // visually compressed the 5 m setpoint signature into a
    // pinned-to-zero line. Dropping CHOPS lets the chart auto-
    // scale to the pair's range — the 5 m line then reads cleanly.

    private static async Task SeedSagdPairJobAsync(
        TenantDbContext db,
        ISurveyAutoCalculator autoCalculator,
        TenantSeedSpec spec,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var job = new Job(
            name:        spec.JobName,
            description: spec.JobDescription,
            unitSystem:  spec.UnitSystem)
        {
            Status         = JobStatus.Active,
            Region         = spec.Region,
            WellName       = "Cold Lake Pad-7 P1",
            StartTimestamp = now,
            EndTimestamp   = now.AddMonths(2),
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);

        // Producer (lower, target). Drilled first; the injector then
        // ranges off the producer's casing magnetically to land 5 m
        // above it.
        var producer = new Well(job.Id, "Cold Lake Pad-7 P1", WellType.Target);
        // Injector (upper, paired by ranging).
        var injector = new Well(job.Id, "Cold Lake Pad-7 I1", WellType.Injection);

        db.Wells.AddRange(producer, injector);
        await db.SaveChangesAsync(ct);

        // SAGD geometry chosen to reproduce the canonical "5 m apart
        // for 700 m" picture. Pay zone TVD ≈ 470 m (typical Cold Lake
        // / Foster Creek depth); injector lands at TVD 465 m,
        // producer at 470 m. Lateral azimuth east (90°). Both wells
        // fully horizontal at landing.
        //
        // Both share the same surface (Northing, Easting) — the
        // 5 m separation comes purely from differentiated KOP depth,
        // so the pair runs **vertically** stacked through the
        // lateral (the actual SAGD geometry). An earlier version
        // gave the injector a 5 m surface Easting offset which
        // produced a horizontal-only 5 m separation (geometrically
        // wrong for SAGD — steam wouldn't fall into the producer).
        SeedSagdHorizontalWell(db, producer.Id,
            tieOnNorthing: spec.SurfaceNorthing,
            tieOnEasting:  spec.SurfaceEasting,
            landingTvd:    470.0);
        SeedSagdHorizontalWell(db, injector.Id,
            tieOnNorthing: spec.SurfaceNorthing,
            tieOnEasting:  spec.SurfaceEasting,
            landingTvd:    465.0);

        SeedWellMagnetics(db, producer.Id, spec);
        SeedWellMagnetics(db, injector.Id, spec);

        await db.SaveChangesAsync(ct);

        await autoCalculator.RecalculateAsync(db, producer.Id, ct);
        await autoCalculator.RecalculateAsync(db, injector.Id, ct);
    }

    /// <summary>
    /// One leg of a SAGD pair — vertical to KOP, build to 90° over
    /// a 200-m MD window so the well lands at <paramref name="landingTvd"/>,
    /// then hold horizontal east for 700 m of lateral. KOP depth is
    /// derived from <paramref name="landingTvd"/> so the producer
    /// (landingTvd 470) and injector (landingTvd 465) end up 5 m
    /// apart vertically throughout the lateral.
    ///
    /// <para>
    /// Linear-inc-vs-MD build adds (2/π) ≈ 0.6366 of the build-MD
    /// length as TVD; with a fixed 200 m build window that's 127.3 m
    /// of TVD added during build. KOP is therefore set 127.3 m
    /// shallower than landingTvd. An earlier version used a fixed
    /// KOP of 300 m which made the wells land at TVD ~427 m
    /// (above the McMurray pay zone), and the parallel-shape
    /// produced a horizontal-only 5 m separation rather than the
    /// vertical separation SAGD actually relies on.
    /// </para>
    /// </summary>
    private static void SeedSagdHorizontalWell(
        TenantDbContext db,
        int wellId,
        double tieOnNorthing,
        double tieOnEasting,
        double landingTvd)
    {
        db.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0)
        {
            Northing                 = tieOnNorthing,
            Easting                  = tieOnEasting,
            VerticalReference        = 0,
            SubSeaReference          = 0,
            VerticalSectionDirection = 90,                          // east — the lateral direction
        });

        // Derive KOP from landingTvd so the wells actually land
        // where requested. With buildMd=200, TVD added during build
        // ≈ 0.6366 * 200 = 127.3 m; KOP is 127.3 m shallower than
        // landingTvd. Lateral hold is 700 m at constant TVD.
        const double buildMd = 200.0;
        var tvdGainPerBuild  = (2.0 / System.Math.PI) * buildMd;    // ≈ 127.3 m for buildMd=200
        var kop              = landingTvd - tvdGainPerBuild;
        var landing          = kop + buildMd;
        var totalMd          = landing + 700.0;

        var stations = new (double Depth, double Inc, double Az)[]
        {
            ( 100.0,                0.0, 90),
            ( 200.0,                0.0, 90),
            ( kop,                  0.5, 90),                       // KOP — start of build
            ( kop + buildMd * 0.25, 22.5, 90),
            ( kop + buildMd * 0.50, 45.0, 90),
            ( kop + buildMd * 0.75, 67.5, 90),
            ( landing,              90.0, 90),                       // landing — fully horizontal
            ( landing + 100, 90.0, 90),
            ( landing + 200, 90.0, 90),
            ( landing + 300, 90.0, 90),
            ( landing + 400, 90.0, 90),
            ( landing + 500, 90.0, 90),
            ( landing + 600, 90.0, 90),
            ( totalMd,       90.0, 90),
        };
        foreach (var s in stations)
            db.Surveys.Add(new Survey(wellId, s.Depth, s.Inc, s.Az));

        // Lighter SAGD completion — surface casing through the
        // shallow muskeg / Quaternary, then a single intermediate
        // string to TD. Slotted liner over the lateral (omitted
        // here; the demo doesn't need slotted-liner semantics).
        db.Tubulars.AddRange(
            new Tubular(wellId, order: 0, type: TubularType.Casing,
                fromMeasured: 0, toMeasured: 200.0,
                diameter: 0.244475, weight: 69.94)                  //  9.625 in / 47 lb/ft
            { Name = "Surface casing" },
            new Tubular(wellId, order: 1, type: TubularType.Casing,
                fromMeasured: 0, toMeasured: totalMd,
                diameter: 0.1778, weight: 38.69)                    //  7.0 in / 26 lb/ft
            { Name = "Production casing" });
    }

    // =================================================================
    // Wytch Farm M-series ERD pair
    // =================================================================
    //
    // BP's Wytch Farm onshore Dorset (UK) extended-reach pad — for
    // years held the world ERD record. Single onshore drilling pad
    // in the Purbeck heathland; wells drill ~10 km laterally
    // southeast under Poole Bay to the Sherwood Sandstone reservoir
    // beneath the English Channel. The point is to develop offshore
    // hydrocarbons without an offshore platform — environmentally
    // sensitive area; "ERD as alternative to offshore facilities"
    // is the case study.
    //
    // Two-well demo: M-11 (the canonical one drilled 1999) and M-16
    // (a later twin). Same trajectory shape, parallel laterals
    // ~50 m apart. On the plan view the chart axes stretch to
    // ~10 km — a stress test for the rendering side compared to
    // the ~3 km of the standard parallel-lateral pilot. Vertical
    // section shows the textbook ERD profile: short build, very
    // long tangent.
    //
    // Realism note: gross trajectory parameters (10.7 km step-out,
    // 1.6 km TVD, build profile) are public via BP / OGA papers.
    // Exact survey rows are operator-confidential, so this trajectory
    // is shape-accurate, not row-accurate. See the test plan for the
    // expected demo signatures.

    private static async Task SeedWytchFarmErdJobAsync(
        TenantDbContext db,
        ISurveyAutoCalculator autoCalculator,
        TenantSeedSpec spec,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var job = new Job(
            name:        "Wytch-Farm-M-Series",
            description: "Seed job — Wytch Farm M-series ERD demo. Two extended-reach wells from one onshore pad reaching ~10.7 km laterally beneath Poole Bay to the Sherwood reservoir. Shape-accurate per BP/OGA refs.",
            unitSystem:  spec.UnitSystem)
        {
            Status         = JobStatus.Active,
            Region         = "UK Onshore — Wytch Farm (Dorset)",
            WellName       = "M-11",
            StartTimestamp = now,
            EndTimestamp   = now.AddMonths(8),
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);

        // Both wells from the same onshore pad. Surface coords
        // offset 12 km from the tenant's primary surface site so
        // the pad sits at visibly distinct grid coords from the
        // parallel-lateral Job. Approach azimuth 135° (south-east)
        // matches the real Wytch Farm reservoir trend (Sherwood
        // formation lies SE of the pad, under Poole Bay).
        var padN = spec.SurfaceNorthing - 12_000.0;
        var padE = spec.SurfaceEasting  + 8_000.0;
        const double approachAz = 135.0;                            // SE — toward the reservoir

        var m11 = new Well(job.Id, "M-11", WellType.Target);
        var m16 = new Well(job.Id, "M-16", WellType.Injection);
        db.Wells.AddRange(m11, m16);
        await db.SaveChangesAsync(ct);

        SeedWytchFarmErdWell(db, m11.Id,
            tieOnNorthing: padN,
            tieOnEasting:  padE,
            approachAz:    approachAz);
        SeedWytchFarmErdWell(db, m16.Id,
            // Surface 50 m offset on the pad. Both wells run
            // ~parallel laterals — on the cylinder plot from M-11
            // with M-16 as offset, distance stays close to the
            // surface offset throughout the lateral.
            tieOnNorthing: padN + 35.0,
            tieOnEasting:  padE + 35.0,
            approachAz:    approachAz);

        SeedWellMagnetics(db, m11.Id, spec);
        SeedWellMagnetics(db, m16.Id, spec);

        await db.SaveChangesAsync(ct);

        await autoCalculator.RecalculateAsync(db, m11.Id, ct);
        await autoCalculator.RecalculateAsync(db, m16.Id, ct);
    }

    /// <summary>
    /// One Wytch-Farm-style ERD well. Vertical to ~1 000 m TVD, build
    /// to 87° between 1 000 → 1 500 m TVD, hold the long tangent at
    /// 87° on <paramref name="approachAz"/> for ~10 km of lateral.
    /// Total MD ~11 500 m. The textbook ERD profile.
    /// </summary>
    private static void SeedWytchFarmErdWell(
        TenantDbContext db,
        int wellId,
        double tieOnNorthing,
        double tieOnEasting,
        double approachAz)
    {
        db.TieOns.Add(new TieOn(wellId, depth: 0, inclination: 0, azimuth: 0)
        {
            Northing                 = tieOnNorthing,
            Easting                  = tieOnEasting,
            VerticalReference        = 0,
            SubSeaReference          = 0,
            VerticalSectionDirection = approachAz,
        });

        var stations = new (double Depth, double Inc, double Az)[]
        {
            // Vertical section.
            (  300, 0.3, approachAz),
            (  600, 0.3, approachAz),
            (  900, 0.4, approachAz),
            ( 1000, 0.5, approachAz),                               // KOP

            // Build to 87° by 1 800 m MD.
            ( 1100,  10.0, approachAz),
            ( 1200,  25.0, approachAz),
            ( 1400,  50.0, approachAz),
            ( 1600,  72.0, approachAz),
            ( 1800,  87.0, approachAz),                             // landing — start of long tangent

            // Long tangent at 87° — the workhorse section. ~9.5 km
            // of MD at 87° gives ~9 480 m of lateral displacement
            // and only ~500 m of additional TVD.
            ( 2500, 87.0, approachAz),
            ( 3500, 87.0, approachAz),
            ( 4500, 87.0, approachAz),
            ( 5500, 87.0, approachAz),
            ( 6500, 87.0, approachAz),
            ( 7500, 87.0, approachAz),
            ( 8500, 87.0, approachAz),
            ( 9500, 87.0, approachAz),
            (10500, 87.0, approachAz),
            (11400, 87.0, approachAz),                              // TD — ~10.7 km step-out
        };
        foreach (var s in stations)
            db.Surveys.Add(new Survey(wellId, s.Depth, s.Inc, s.Az));

        // Single intermediate string + a long production liner to TD —
        // a typical ERD completion. Shoulder-of-build casing point
        // (1 800 m MD, end of build) is the workhorse anti-collision
        // anchor so we set casing to that depth.
        db.Tubulars.AddRange(
            new Tubular(wellId, order: 0, type: TubularType.Casing,
                fromMeasured: 0, toMeasured: 1_800.0,
                diameter: 0.244475, weight: 69.94)                  //  9.625 in / 47 lb/ft
            { Name = "Intermediate casing (to landing)" },
            new Tubular(wellId, order: 1, type: TubularType.Liner,
                fromMeasured: 1_700.0, toMeasured: 11_400.0,
                diameter: 0.1778, weight: 38.69)                    //  7.0 in / 26 lb/ft
            { Name = "ERD production liner" });
    }
}
