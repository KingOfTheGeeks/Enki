using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Jobs.Enums;
using SDI.Enki.Core.TenantDb.Wells;
using SDI.Enki.Core.TenantDb.Wells.Enums;
using SDI.Enki.Core.Units;
using SDI.Enki.Infrastructure.Data;

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
///   <item><b>Target</b> (Johnson 1H): Permian-Basin horizontal producer, ISCWSA Well 3 shape — vertical to ~3000 ft, build to 90°, hold lateral to 10,000 ft MD.</item>
///   <item><b>Injection</b> (Johnson 1I): adjacent parallel injection lateral 50 ft below the producer (CO2 / water-flood pattern). Same trajectory shape, slightly offset depths.</item>
///   <item><b>Offset</b> (Smith Federal 1): older vertical neighbour for anti-collision, ISCWSA Well 1 shape — sparse stations, &lt;2° inclination drift.</item>
/// </list>
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
    public static async Task SeedAsync(TenantDbContext db, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // ---------- Job ----------
        var job = new Job(
            name:        "Permian-22-14H",
            description: "Seed job — horizontal lateral pilot, ~10,000 ft MD.",
            unitSystem:  UnitSystem.Field)
        {
            Status         = JobStatus.Active,
            Region         = "Permian Basin",
            WellName       = "Johnson 1H",
            StartTimestamp = now,
            EndTimestamp   = now.AddMonths(3),
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);   // need job.Id for the Well FKs

        // ---------- Wells ----------
        var target    = new Well(job.Id, "Johnson 1H",       WellType.Target);
        var injector  = new Well(job.Id, "Johnson 1I",       WellType.Injection);
        var offset    = new Well(job.Id, "Smith Federal 1",  WellType.Offset);
        db.Wells.AddRange(target, injector, offset);
        await db.SaveChangesAsync(ct);   // need well.Ids for child rows

        SeedTargetWell(db, target.Id);
        SeedInjectorWell(db, injector.Id);
        SeedOffsetWell(db, offset.Id);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Johnson 1H — the horizontal producer. Full survey set + casing
    /// stack + formations + mud-weight profile. ISCWSA-Well-3 shape.
    /// </summary>
    private static void SeedTargetWell(TenantDbContext db, int wellId)
    {
        // Surface tie-on right below conductor / surface casing.
        db.TieOns.Add(new TieOn(wellId, depth: 200, inclination: 0.5, azimuth: 180)
        {
            Northing                 = 1_500_000,
            Easting                  =   600_000,
            VerticalReference        = 200,
            SubSeaReference          =   0,
            VerticalSectionDirection = 180,
        });

        // Vertical → kick-off → horizontal, 10 stations at 1000 ft.
        // Computed columns (TVD / DLS / …) populate when the user clicks
        // Calculate; the seed leaves them at zero.
        var stations = new (double Depth, double Inc, double Az)[]
        {
            (1000,   0.5, 180),
            (2000,   1.5, 180),
            (3000,  10.0, 180),
            (4000,  35.0, 180),
            (5000,  65.0, 180),
            (6000,  88.0, 180),
            (7000,  90.0, 180),
            (8000,  90.0, 180),
            (9000,  90.0, 180),
            (10000, 90.0, 180),
        };
        foreach (var s in stations)
            db.Surveys.Add(new Survey(wellId, s.Depth, s.Inc, s.Az));

        // Three-string design: surface → intermediate → production liner.
        db.Tubulars.AddRange(
            new Tubular(wellId, order: 0, type: TubularType.Casing,
                fromMeasured: 0, toMeasured: 2000,
                diameter: 13.375, weight: 68)
            { Name = "Surface casing" },
            new Tubular(wellId, order: 1, type: TubularType.Casing,
                fromMeasured: 0, toMeasured: 5000,
                diameter: 9.625, weight: 47)
            { Name = "Intermediate casing" },
            new Tubular(wellId, order: 2, type: TubularType.Liner,
                fromMeasured: 4500, toMeasured: 10_000,
                diameter: 5.5, weight: 17)
            { Name = "Production liner" });

        // Three classic Permian / Texas tops along the vertical section.
        db.Formations.AddRange(
            new Formation(wellId, "Austin Chalk",
                fromVertical: 2500, toVertical: 3500, resistance: 12)
            { Description = "Carbonate, secondary target" },
            new Formation(wellId, "Buda",
                fromVertical: 3500, toVertical: 4500, resistance: 15)
            { Description = "Limestone seal above Eagle Ford" },
            new Formation(wellId, "Eagle Ford",
                fromVertical: 4500, toVertical: 5500, resistance: 8)
            { Description = "Source rock — primary target zone" });

        // Mud-weight profile by depth — steps up through the
        // overpressured Eagle Ford section, holds in the lateral.
        db.CommonMeasures.AddRange(
            new CommonMeasure(wellId, fromVertical:    0, toVertical: 2000, value:  9.0),
            new CommonMeasure(wellId, fromVertical: 2000, toVertical: 4500, value:  9.5),
            new CommonMeasure(wellId, fromVertical: 4500, toVertical: 7000, value: 11.5),
            new CommonMeasure(wellId, fromVertical: 7000, toVertical: 10_000, value: 12.5));
    }

    /// <summary>
    /// Johnson 1I — parallel injection lateral 50 ft below the producer.
    /// Lighter footprint than the target: tie-on, survey set, two
    /// tubulars (injection wells use simpler completions). No
    /// formations / common-measures — those mirror the target's
    /// vertical section and are recorded once on the lead well in
    /// real-world programs.
    /// </summary>
    private static void SeedInjectorWell(TenantDbContext db, int wellId)
    {
        db.TieOns.Add(new TieOn(wellId, depth: 200, inclination: 0.5, azimuth: 180)
        {
            Northing                 = 1_499_950,    // ~50 ft south of the target
            Easting                  =   600_000,
            VerticalReference        = 250,           // 50 ft deeper than target
            SubSeaReference          =   0,
            VerticalSectionDirection = 180,
        });

        // Same vertical-to-horizontal shape, ~50 ft offset by depth so
        // the lateral runs parallel to and below the producer.
        var stations = new (double Depth, double Inc, double Az)[]
        {
            (1000,   0.5, 180),
            (2000,   1.5, 180),
            (3050,  10.0, 180),
            (4050,  35.0, 180),
            (5050,  65.0, 180),
            (6050,  88.0, 180),
            (7050,  90.0, 180),
            (8050,  90.0, 180),
            (9050,  90.0, 180),
            (10050, 90.0, 180),
        };
        foreach (var s in stations)
            db.Surveys.Add(new Survey(wellId, s.Depth, s.Inc, s.Az));

        db.Tubulars.AddRange(
            new Tubular(wellId, order: 0, type: TubularType.Casing,
                fromMeasured: 0, toMeasured: 5050,
                diameter: 9.625, weight: 47)
            { Name = "Injector casing" },
            new Tubular(wellId, order: 1, type: TubularType.Tubing,
                fromMeasured: 0, toMeasured: 10_050,
                diameter: 3.5, weight: 9.3)
            { Name = "Injection tubing" });
    }

    /// <summary>
    /// Smith Federal 1 — older vertical neighbour. Drilled circa 1985,
    /// sparse manual surveys, ~1° drift over ~8000 ft. ISCWSA Well 1
    /// shape. Used here as the anti-collision reference offset; the
    /// active program drills around it.
    /// </summary>
    private static void SeedOffsetWell(TenantDbContext db, int wellId)
    {
        db.TieOns.Add(new TieOn(wellId, depth: 100, inclination: 0.0, azimuth: 0)
        {
            Northing                 = 1_500_500,    // ~500 ft north of the target
            Easting                  =   600_400,
            VerticalReference        = 100,
            SubSeaReference          =   0,
            VerticalSectionDirection = 270,
        });

        // Sparse vertical survey set with mild westward drift, typical
        // of older drilling without rotary-steerable assemblies.
        var stations = new (double Depth, double Inc, double Az)[]
        {
            (1000, 0.3, 268),
            (3000, 0.8, 265),
            (5000, 1.2, 263),
            (7000, 1.6, 262),
            (8000, 1.8, 262),
        };
        foreach (var s in stations)
            db.Surveys.Add(new Survey(wellId, s.Depth, s.Inc, s.Az));

        // One legacy casing string. Anti-collision references rarely
        // need full-fidelity completions data on the existing well —
        // depth + trajectory is what the planner cares about.
        db.Tubulars.Add(new Tubular(wellId, order: 0, type: TubularType.Casing,
            fromMeasured: 0, toMeasured: 8000,
            diameter: 7.0, weight: 26)
        { Name = "Production casing (legacy)" });
    }
}
