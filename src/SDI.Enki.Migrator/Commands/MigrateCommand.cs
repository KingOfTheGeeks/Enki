using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SDI.Enki.Core.Master.Migrations;
using SDI.Enki.Core.Master.Migrations.Enums;
using SDI.Enki.Core.Master.Tenants;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning;
using SDI.Enki.Infrastructure.Provisioning.Models;

namespace SDI.Enki.Migrator.Commands;

internal static class MigrateCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var parser = new ArgParser(args);
        var tenantFilter = parser.GetList("tenants");
        var onlyActive = parser.Has("only-active");
        var onlyArchive = parser.Has("only-archive");
        var maxParallel = Math.Max(1, parser.GetInt("parallel", 4));

        await using var scope = services.CreateAsyncScope();
        var master = scope.ServiceProvider.GetRequiredService<AthenaMasterDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<MigrationRun>>();
        var options = scope.ServiceProvider.GetRequiredService<ProvisioningOptions>();
        var dbAdmin = scope.ServiceProvider.GetRequiredService<DatabaseAdmin>();

        IQueryable<TenantDatabase> query = master.TenantDatabases.Include(x => x.Tenant);
        if (tenantFilter.Length > 0)
            query = query.Where(x => x.Tenant != null && tenantFilter.Contains(x.Tenant.Code));
        if (onlyActive)
            query = query.Where(x => x.Kind == TenantDatabaseKind.Active);
        if (onlyArchive)
            query = query.Where(x => x.Kind == TenantDatabaseKind.Archive);

        var targets = await query.ToListAsync();
        if (targets.Count == 0)
        {
            Console.WriteLine("No tenant databases match the filter — nothing to migrate.");
            return 0;
        }

        Console.WriteLine($"Migrating {targets.Count} database(s) with up to {maxParallel} in parallel...");
        var sem = new SemaphoreSlim(maxParallel);
        var results = new List<(string Label, bool Success, string Detail)>();
        var resultsLock = new object();

        var tasks = targets.Select(async target =>
        {
            await sem.WaitAsync();
            try
            {
                var (ok, detail) = await MigrateOneAsync(target, options, dbAdmin, logger);
                var label = $"{target.Tenant?.Code ?? "?"}/{target.Kind.Name}";
                lock (resultsLock) results.Add((label, ok, detail));
            }
            finally
            {
                sem.Release();
            }
        });
        await Task.WhenAll(tasks);

        Console.WriteLine();
        foreach (var (label, ok, detail) in results.OrderBy(r => r.Label))
            Console.WriteLine($"  [{(ok ? "OK " : "FAIL")}] {label,-32} {detail}");

        var failed = results.Count(r => !r.Success);
        Console.WriteLine();
        Console.WriteLine($"Summary: {results.Count - failed} succeeded, {failed} failed.");
        return failed == 0 ? 0 : 2;
    }

    /// <summary>
    /// Apply EF migrations to one tenant DB. Writes a MigrationRun audit row
    /// around each attempt. For Archive DBs, flips READ_ONLY off before and
    /// back on after the migration.
    /// </summary>
    private static async Task<(bool Success, string Detail)> MigrateOneAsync(
        TenantDatabase dbRow,
        ProvisioningOptions options,
        DatabaseAdmin dbAdmin,
        ILogger logger)
    {
        // Each worker gets its own short-lived master context (cross-thread safety).
        var masterOpts = new DbContextOptionsBuilder<AthenaMasterDbContext>()
            .UseSqlServer(options.MasterConnectionString)
            .Options;
        await using var master = new AthenaMasterDbContext(masterOpts);

        var run = new MigrationRun(dbRow.TenantId, dbRow.Kind, "latest");
        master.MigrationRuns.Add(run);
        await master.SaveChangesAsync();

        try
        {
            if (dbRow.Kind == TenantDatabaseKind.Archive)
                await dbAdmin.SetReadWriteAsync(dbRow.DatabaseName);

            var tenantConn = dbAdmin.BuildTenantConnectionString(dbRow.DatabaseName);
            var tenantOpts = new DbContextOptionsBuilder<TenantDbContext>()
                .UseSqlServer(tenantConn)
                .Options;
            await using var tenantDb = new TenantDbContext(tenantOpts);
            await tenantDb.Database.MigrateAsync();

            var applied = (await tenantDb.Database.GetAppliedMigrationsAsync())
                .LastOrDefault() ?? "unknown";

            if (dbRow.Kind == TenantDatabaseKind.Archive)
                await dbAdmin.SetReadOnlyAsync(dbRow.DatabaseName);

            run.Status = MigrationRunStatus.Success;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.TargetVersion = applied;

            // Attach + update dbRow via a fresh load so EF tracks it here.
            var rowRef = await master.TenantDatabases
                .Where(x => x.TenantId == dbRow.TenantId && x.Kind == dbRow.Kind)
                .FirstOrDefaultAsync();
            if (rowRef is not null)
            {
                rowRef.SchemaVersion = applied;
                rowRef.LastMigrationAt = DateTimeOffset.UtcNow;
            }

            await master.SaveChangesAsync();
            return (true, $"-> {applied}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Migration failed for {DatabaseName}", dbRow.DatabaseName);
            run.Status = MigrationRunStatus.Failed;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.Error = ex.Message;

            // Re-flip Archive to READ_ONLY if we got as far as toggling it.
            if (dbRow.Kind == TenantDatabaseKind.Archive)
            {
                try { await dbAdmin.SetReadOnlyAsync(dbRow.DatabaseName); }
                catch { /* best-effort */ }
            }

            try { await master.SaveChangesAsync(); }
            catch { /* best-effort */ }

            return (false, ex.Message);
        }
    }
}
