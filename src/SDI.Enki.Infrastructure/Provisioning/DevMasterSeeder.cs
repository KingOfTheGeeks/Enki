using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning.Models;

namespace SDI.Enki.Infrastructure.Provisioning;

/// <summary>
/// Dev-only idempotent auto-provision for a known demo tenant. Runs at
/// host startup when <see cref="ProvisioningOptions.SeedSampleData"/> is
/// on; skips with no side effect when off or when the tenant already
/// exists.
///
/// <para>
/// Pairs with <see cref="DevTenantSeeder"/>: this side creates the
/// tenant registry rows + provisions the two databases, that side fills
/// the Active DB with demo Jobs. Together they mean a fresh dev machine
/// boots into a working multi-tenant state without any manual clicks.
/// </para>
///
/// <para>
/// Failures are logged and swallowed — a provisioning problem (SQL Server
/// down, master DB schema behind, etc.) should not crash the host.
/// Users can recover by provisioning manually through the UI.
/// </para>
/// </summary>
public static class DevMasterSeeder
{
    /// <summary>Canonical dev tenant code. Also appears in punch-lists and docs.</summary>
    public const string DemoTenantCode = "TENANTTEST";

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

            var master = sp.GetRequiredService<EnkiMasterDbContext>();
            var exists = await master.Tenants
                .AsNoTracking()
                .AnyAsync(t => t.Code == DemoTenantCode, ct);

            if (exists)
            {
                logger.LogDebug("DevMasterSeeder skipped — tenant {Code} already exists.", DemoTenantCode);
                return;
            }

            var provisioning = sp.GetRequiredService<ITenantProvisioningService>();

            var result = await provisioning.ProvisionAsync(
                new ProvisionTenantRequest(
                    Code:         DemoTenantCode,
                    Name:         "Tenant Test Demo",
                    DisplayName:  "Demo",
                    ContactEmail: null,
                    Notes:        "Auto-seeded by DevMasterSeeder. Safe to deprovision and let the next boot recreate it."),
                ct);

            logger.LogInformation(
                "DevMasterSeeder provisioned {Code} ({TenantId}) — Active={Active}, Archive={Archive}, Schema={Schema}",
                result.Code, result.TenantId,
                result.ActiveDatabaseName, result.ArchiveDatabaseName, result.AppliedSchemaVersion);
        }
        catch (Exception ex)
        {
            // Logger may not have been resolved yet (e.g., ServiceProvider
            // itself was broken). Fall back to Console so the failure is
            // still visible without dumping a stack trace into the host's
            // fatal-error path.
            if (logger is not null)
                logger.LogWarning(ex,
                    "DevMasterSeeder failed. Host will continue; provision {Code} manually via the UI if desired.",
                    DemoTenantCode);
            else
                Console.Error.WriteLine(
                    $"[DevMasterSeeder] swallowed startup error ({ex.GetType().Name}): {ex.Message}");
        }
    }
}
