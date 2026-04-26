using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Jobs.Enums;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning.Models;
using SDI.Enki.Infrastructure.Surveys;

namespace SDI.Enki.Infrastructure.Provisioning;

/// <summary>
/// Seeds a freshly-provisioned tenant's Active DB with realistic demo
/// content — one Job, three Wells (one of each <see cref="WellType"/>),
/// each with its own tie-on, surveys, and (for the active producer) a
/// full casing / formation / mud-weight stack. Three wells lets every
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
///   <item><b>Target</b> (Johnson 1H): Permian-Basin horizontal producer, ISCWSA Well 3 shape — vertical to ~914 m, build to 90°, hold lateral to 3048 m MD (10 000 ft in the source spec).</item>
///   <item><b>Injection</b> (Johnson 1I): adjacent parallel injection lateral ~15 m below the producer (CO2 / water-flood pattern). Same trajectory shape, slightly offset depths.</item>
///   <item><b>Offset</b> (Smith Federal 1): older vertical neighbour for anti-collision, ISCWSA Well 1 shape — sparse stations, &lt;2° inclination drift.</item>
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
/// 1 ft = 0.3048 m, 1 in = 0.0254 m, 1 lb/ft = 1.488164 kg/m,
/// 1 ppg = 119.826427 kg/m³.
/// </para>
///
/// <para>
/// Gated on <see cref="Models.ProvisioningOptions.SeedSampleData"/>:
/// WebApi turns it on in Development, Migrator CLI + prod hosts leave
/// it off. <see cref="DevMasterSeeder"/> is the only caller that sets
/// the per-request flag, so this content lands only inside the
/// bootstrap TENANTTEST tenant.
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
        ISurveyAutoCalculator autoCalculator,
        TenantSeedSpec spec,
        CancellationToken ct = default)
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
    /// Johnson 1H — the horizontal producer. Full survey set + casing
    /// stack + formations + mud-weight profile. ISCWSA-Well-3 shape.
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
        // Depths in m; resistance in ohm-m (already SI).
        db.Formations.AddRange(
            new Formation(wellId, "Austin Chalk",
                fromVertical: 762.0, toVertical: 1066.8, resistance: 12)   //  2 500 / 3 500 ft
            { Description = "Carbonate, secondary target" },
            new Formation(wellId, "Buda",
                fromVertical: 1066.8, toVertical: 1371.6, resistance: 15)  //  3 500 / 4 500 ft
            { Description = "Limestone seal above Eagle Ford" },
            new Formation(wellId, "Eagle Ford",
                fromVertical: 1371.6, toVertical: 1676.4, resistance: 8)   //  4 500 / 5 500 ft
            { Description = "Source rock — primary target zone" });

        // Mud-weight profile by depth — steps up through the overpressured
        // Eagle Ford section, holds in the lateral. Depths in m,
        // mud weight in kg/m³ (source ppg × 119.826427).
        db.CommonMeasures.AddRange(
            new CommonMeasure(wellId, fromVertical:    0.0, toVertical:  609.6, value: 1078.44),  //  9.0 ppg
            new CommonMeasure(wellId, fromVertical:  609.6, toVertical: 1371.6, value: 1138.35),  //  9.5 ppg
            new CommonMeasure(wellId, fromVertical: 1371.6, toVertical: 2133.6, value: 1378.00),  // 11.5 ppg
            new CommonMeasure(wellId, fromVertical: 2133.6, toVertical: 3048.0, value: 1497.83)); // 12.5 ppg
    }

    /// <summary>
    /// Johnson 1I — parallel injection lateral ~15 m below the producer
    /// (50 ft offset in the source spec). Lighter footprint than the
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
    /// Smith Federal 1 — older vertical neighbour. Drilled circa 1985,
    /// sparse manual surveys, ~1° drift over ~2438 m (8000 ft). ISCWSA
    /// Well 1 shape. Used here as the anti-collision reference offset;
    /// the active program drills around it.
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
}
