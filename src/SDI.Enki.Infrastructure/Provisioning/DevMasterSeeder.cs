using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SDI.Enki.Core.Units;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning.Models;

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
/// fresh dev machine boots into a working multi-tenant state — three
/// tenants visible in the UI — without any manual clicks.
/// </para>
///
/// <para>
/// Three demo tenants ship today: TENANTTEST (Permian Basin), BAKKEN
/// (Williston), NORTHSEA (offshore UK). The trajectory math is
/// identical across all three; only the tenant code, well names,
/// region label, unit-system preference, and surface coordinates
/// differ. That keeps the seed surface tractable while still
/// exercising the multi-tenant click-through paths in the UI.
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
    /// <summary>Canonical bootstrap tenant code. Also appears in punch-lists and docs.</summary>
    public const string DemoTenantCode = "TENANTTEST";

    /// <summary>
    /// Curated demo-tenant roster. Order is wire-stable — the UI's
    /// Tenants list shows them in this order on a fresh boot.
    /// </summary>
    public static readonly IReadOnlyList<TenantSeedSpec> DemoTenants =
    [
        // Permian Basin — the bootstrap demo. Coords match the
        // 1 500 000 ft / 600 000 ft Texas state-plane baseline that
        // the original seed used.
        new TenantSeedSpec(
            Code:             DemoTenantCode,
            Name:             "Tenant Test Demo",
            DisplayName:      "Demo",
            Notes:            "Auto-seeded by DevMasterSeeder. Permian Basin demo. Safe to deprovision and let the next boot recreate it.",
            JobName:          "Permian-22-14H",
            JobDescription:   "Seed job — horizontal lateral pilot, ~3048 m MD (10 000 ft).",
            Region:           "Permian Basin",
            UnitSystem:       UnitSystem.Field,
            TargetWellName:   "Johnson 1H",
            InjectorWellName: "Johnson 1I",
            OffsetWellName:   "Smith Federal 1",
            SurfaceNorthing:  457_200,    // 1 500 000 ft Texas state plane
            SurfaceEasting:   182_880),   //   600 000 ft

        // Bakken Shale, North Dakota. Coords near the Williston
        // Basin core (UTM 13N order of magnitude). Lambert 2H/I
        // is the producer + injector pair; Pearson 1 is the legacy
        // anti-collision offset.
        new TenantSeedSpec(
            Code:             "BAKKEN",
            Name:             "Bakken Operations",
            DisplayName:      "Bakken",
            Notes:            "Auto-seeded by DevMasterSeeder. Williston Basin / Bakken Shale demo.",
            JobName:          "Williston-25-3H",
            JobDescription:   "Seed job — Bakken horizontal pilot, parallel laterals to ~3050 m MD.",
            Region:           "Williston Basin",
            UnitSystem:       UnitSystem.Field,
            TargetWellName:   "Lambert 2H",
            InjectorWellName: "Lambert 2I",
            OffsetWellName:   "Pearson 1",
            SurfaceNorthing:  5_300_000,
            SurfaceEasting:     580_000),

        // North Sea / UKCS. Coords near the Brent field (UTM 31N
        // order of magnitude). Metric (m / bar / °C / kg/m³)
        // matches the operational convention offshore UK — strict
        // SI would render mud weight in pascals which nobody uses
        // on a live rig.
        new TenantSeedSpec(
            Code:             "NORTHSEA",
            Name:             "North Sea Operations",
            DisplayName:      "North Sea",
            Notes:            "Auto-seeded by DevMasterSeeder. North Sea / Brent field offshore demo.",
            JobName:          "Brent-26-7H",
            JobDescription:   "Seed job — Brent field horizontal redevelopment, ~3050 m MD.",
            Region:           "North Sea — UKCS",
            UnitSystem:       UnitSystem.Metric,
            TargetWellName:   "Brent A-12",
            InjectorWellName: "Brent A-13",
            OffsetWellName:   "Brent A-7",
            SurfaceNorthing:  6_700_000,
            SurfaceEasting:     460_000),
    ];

    public static async Task SeedAsync(
        IServiceProvider services,
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

            if (!options.SeedSampleData)
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
                    var result = await provisioning.ProvisionAsync(
                        new ProvisionTenantRequest(
                            Code:           spec.Code,
                            Name:           spec.Name,
                            DisplayName:    spec.DisplayName,
                            ContactEmail:   null,
                            Notes:          spec.Notes,
                            SeedSampleData: true,
                            SeedSpec:       spec),
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
}
