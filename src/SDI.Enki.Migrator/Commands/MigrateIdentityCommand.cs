using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SDI.Enki.Identity.Data;
using SDI.Enki.Shared.Configuration;

namespace SDI.Enki.Migrator.Commands;

/// <summary>
/// Applies EF Core migrations to the Identity DB. Idempotent — re-runs
/// after host updates are no-ops once the migrations are present.
///
/// <para>
/// Required configuration: <c>ConnectionStrings:Identity</c>. The
/// command validates the key up front via
/// <see cref="RequiredSecretsValidator"/>; in non-Development
/// environments a missing connection string fails before EF gets a
/// chance to throw a less informative error.
/// </para>
/// </summary>
internal static class MigrateIdentityCommand
{
    public static async Task<int> RunAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        try
        {
            RequiredSecretsValidator.Validate(
                configuration, environment,
                required:
                [
                    new("ConnectionStrings:Identity",
                        "Identity DB connection string."),
                ]);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        await using var scope = services.CreateAsyncScope();
        var db     = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Enki.Migrator.MigrateIdentity");

        try
        {
            logger.LogInformation("Applying Identity DB migrations...");
            await db.Database.MigrateAsync();
            var applied = (await db.Database.GetAppliedMigrationsAsync()).LastOrDefault() ?? "(none)";
            Console.WriteLine($"Identity DB migrated. Last applied: {applied}");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Identity DB migration failed.");
            Console.Error.WriteLine($"Identity migration FAILED: {ex.Message}");
            return 2;
        }
    }
}
