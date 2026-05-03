using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SDI.Enki.Identity.Data;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning;

namespace SDI.Enki.Migrator.Commands;

/// <summary>
/// Dev-only first-boot bootstrap. Mirrors what the previous
/// host-startup gates did: migrate Identity DB, migrate Master DB +
/// run the canonical Tools/Calibrations seed, seed the full
/// <c>SeedUsers</c> roster + OpenIddict client (via the
/// <see cref="IdentitySeedData"/> shim — dev fallback creds), and
/// auto-provision the demo tenant set (PERMIAN / NORTHSEA / BOREAL).
///
/// <para>
/// Refuses to run outside Development: <see cref="IdentitySeedData"/>'s
/// <c>ResolveCredential</c> throws when a non-Dev environment is
/// detected without the seed config keys present, so this command's
/// fail-loud posture against accidental prod use comes from the
/// shim — no extra check needed here.
/// </para>
///
/// <para>
/// Used by <c>start-dev.ps1 -Reset</c> as the single replacement for
/// the host-startup auto-bootstrap that <c>plan-migrator-bootstrap.md</c>
/// retired in Workstream D.
/// </para>
/// </summary>
internal static class DevBootstrapCommand
{
    public static async Task<int> RunAsync(
        IServiceProvider services,
        IHostEnvironment environment)
    {
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Enki.Migrator.DevBootstrap");

        if (!environment.IsDevelopment())
        {
            Console.Error.WriteLine(
                "dev-bootstrap is for the local dev rig only and refuses to " +
                $"run in environment '{environment.EnvironmentName}'. Use " +
                "bootstrap-environment for non-Dev environments.");
            return 1;
        }

        try
        {
            // 1. Identity schema.
            logger.LogInformation("dev-bootstrap step 1/4 — Identity DB migrations...");
            await using (var scope = services.CreateAsyncScope())
            {
                var idDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await idDb.Database.MigrateAsync();
            }

            // 2. Master schema + Tools / Calibrations seed.
            logger.LogInformation("dev-bootstrap step 2/4 — Master DB migrations + Tools/Calibrations seed...");
            await using (var scope = services.CreateAsyncScope())
            {
                var masterDb = scope.ServiceProvider.GetRequiredService<EnkiMasterDbContext>();
                await masterDb.Database.MigrateAsync();
                await MasterDataSeeder.SeedAsync(masterDb, logger);
            }

            // 3. Identity dev roster + OpenIddict client (dev fallback creds).
            logger.LogInformation("dev-bootstrap step 3/4 — SeedUsers roster + OpenIddict client...");
            await IdentitySeedData.SeedAsync(services);

            // 4. Demo tenants (PERMIAN / NORTHSEA / BOREAL).
            logger.LogInformation("dev-bootstrap step 4/4 — demo tenant provisioning...");
            await DevMasterSeeder.SeedAsync(services, force: true);

            Console.WriteLine();
            Console.WriteLine("Dev environment bootstrap complete.");
            Console.WriteLine("  - Identity DB migrated, SeedUsers roster ready (Mike / Gavin / etc.)");
            Console.WriteLine("  - Master DB migrated, Tools + Calibrations seeded");
            Console.WriteLine("  - Demo tenants ensured (PERMIAN / NORTHSEA / BOREAL)");
            Console.WriteLine();
            Console.WriteLine("Start the hosts via start-dev.ps1 (or `dotnet run` per project).");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "dev-bootstrap FAILED.");
            Console.Error.WriteLine($"dev-bootstrap FAILED: {ex.Message}");
            return 2;
        }
    }
}
