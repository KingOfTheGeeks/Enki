using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SDI.Enki.Infrastructure.Provisioning;

namespace SDI.Enki.Migrator.Commands;

/// <summary>
/// Dev-only convenience: provision the curated demo tenant set
/// (PERMIAN / NORTHSEA / BOREAL) plus their seed data via
/// <see cref="DevMasterSeeder"/>. Idempotent — tenants that already
/// exist are skipped.
///
/// <para>
/// Bypasses the <c>ProvisioningOptions.SeedSampleData</c> gate by
/// passing <c>force: true</c> to <see cref="DevMasterSeeder.SeedAsync"/>;
/// the operator running this command is the source of truth, not the
/// host-startup config flag (which is for the WebApi auto-provision
/// path that this command is the explicit replacement for).
/// </para>
///
/// <para>
/// Master DB must be migrated first (run <c>migrate-master</c> or
/// <c>bootstrap-environment</c> before this).
/// </para>
/// </summary>
internal static class SeedDemoTenantsCommand
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Enki.Migrator.SeedDemoTenants");

        try
        {
            logger.LogInformation("Provisioning demo tenants (PERMIAN / NORTHSEA / BOREAL)...");
            await DevMasterSeeder.SeedAsync(services, force: true);
            Console.WriteLine("Demo tenants ensured (PERMIAN / NORTHSEA / BOREAL).");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "seed-demo-tenants FAILED.");
            Console.Error.WriteLine($"seed-demo-tenants FAILED: {ex.Message}");
            return 2;
        }
    }
}
