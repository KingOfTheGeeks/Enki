using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning.Internal;

namespace SDI.Enki.Infrastructure.Provisioning;

/// <summary>
/// Dev-only "apply pending tenant migrations on startup" helper.
/// Mirrors the auto-migrate-master-DB block at the top of
/// <c>Program.cs</c> for the per-tenant DBs the master registry knows
/// about.
///
/// <para>
/// Tenant DBs get migrated at provisioning time
/// (<c>TenantProvisioningService.ApplyTenantMigrationsAsync</c>); when
/// new schema lands after a tenant has already been provisioned, only
/// a fresh <c>start-dev.ps1 -Reset</c> would otherwise pick it up
/// (Reset wipes + re-provisions everything). For dev convenience this
/// helper iterates every <c>Active</c>-status TenantDatabase row, opens
/// it, and calls <c>Database.MigrateAsync</c> — no-ops if the schema
/// is already current; applies pending migrations otherwise.
/// </para>
///
/// <para>
/// Per-tenant try/catch so one half-broken DB doesn't skip the others
/// (same defence-in-depth as <c>DevMasterSeeder</c>). Non-fatal — if
/// migration fails the WebApi still comes up; the broken tenant just
/// 500s on first access and the user sees the underlying SQL error
/// in the logs. <b>Production hosts must not call this</b>; production
/// migrations go through the Migrator CLI before the WebApi starts.
/// </para>
/// </summary>
public static class DevTenantMigrator
{
    public static async Task MigrateAllAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();

        var master  = scope.ServiceProvider.GetRequiredService<EnkiMasterDbContext>();
        var options = scope.ServiceProvider.GetRequiredService<Models.ProvisioningOptions>();
        var logger  = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                          .CreateLogger("Enki.WebApi.DevTenantMigrate");

        var dbs = await master.TenantDatabases
            .Where(d => d.Status == TenantDatabaseStatus.Active)
            .ToListAsync(ct);

        foreach (var dbRow in dbs)
        {
            try
            {
                var connStr = TenantConnectionStringBuilder.ForTenantDatabase(
                    options.MasterConnectionString, dbRow.DatabaseName);

                var opts = new DbContextOptionsBuilder<TenantDbContext>()
                    .UseSqlServer(connStr, sql => sql.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorNumbersToAdd: null))
                    .Options;

                await using var tenantDb = new TenantDbContext(opts);

                var pending = (await tenantDb.Database.GetPendingMigrationsAsync(ct)).ToList();
                if (pending.Count == 0) continue;

                logger.LogInformation(
                    "Tenant DB {DbName} ({Kind}) has {Count} pending migration(s); applying.",
                    dbRow.DatabaseName, dbRow.Kind.Name, pending.Count);

                await tenantDb.Database.MigrateAsync(ct);

                logger.LogInformation(
                    "Tenant DB {DbName} migrated to {LastApplied}.",
                    dbRow.DatabaseName, pending[^1]);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Tenant DB {DbName} ({Kind}) failed to migrate. The WebApi will still start; " +
                    "this tenant's endpoints may 500 until you re-run scripts/start-dev.ps1 -Reset.",
                    dbRow.DatabaseName, dbRow.Kind.Name);
            }
        }
    }
}
