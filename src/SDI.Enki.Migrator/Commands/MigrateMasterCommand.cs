using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.Migrator.Commands;

/// <summary>
/// Applies EF Core migrations to the Master DB and runs the canonical
/// Tools + Calibrations seed (<see cref="MasterDataSeeder"/>) so the
/// app has a working tool fleet to bind licenses to. Both steps are
/// idempotent.
///
/// <para>
/// Master connection string is already enforced at Migrator startup
/// (<see cref="Program"/> exits before dispatching if it's missing),
/// so this command doesn't re-check it.
/// </para>
/// </summary>
internal static class MigrateMasterCommand
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var master = scope.ServiceProvider.GetRequiredService<EnkiMasterDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Enki.Migrator.MigrateMaster");

        try
        {
            logger.LogInformation("Applying Master DB migrations...");
            await master.Database.MigrateAsync();
            var applied = (await master.Database.GetAppliedMigrationsAsync()).LastOrDefault() ?? "(none)";
            logger.LogInformation("Master DB migrated. Last applied: {Migration}", applied);

            // Tool + Calibration fleet seed. Idempotent — no-ops once the
            // tables have rows. Without it, license generation can't bind
            // a generated .lic to a tool and the WebApi's licensing endpoints
            // 404 the tool.
            await MasterDataSeeder.SeedAsync(master, logger);

            Console.WriteLine($"Master DB migrated + seeded. Last applied: {applied}");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Master DB migration failed.");
            Console.Error.WriteLine($"Master migration FAILED: {ex.Message}");
            return 2;
        }
    }
}
