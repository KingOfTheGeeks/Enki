using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Infrastructure.Provisioning;

namespace SDI.Enki.WebApi.Background;

/// <summary>
/// Daily sweep that prunes old per-tenant <c>AuditLog</c> rows from
/// every active tenant's Active database. Distinct from
/// <see cref="MasterAuditRetentionService"/> by topology — the master
/// sweep is one DB; this sweep fans out across the whole tenant
/// fleet.
///
/// <para>
/// <b>Why this lives in the WebApi host:</b> the WebApi already owns
/// the multitenancy plumbing (<see cref="DatabaseAdmin"/>,
/// <c>TenantConnectionStringBuilder</c>, the master DB read path).
/// Adding a separate worker host just to run this sweep would
/// duplicate all of that.
/// </para>
///
/// <para>
/// <b>Why we don't use <see cref="Multitenancy.ITenantDbContextFactory"/>:</b>
/// that factory needs an <c>HttpContext</c> to resolve the current
/// tenant. A <see cref="BackgroundService"/> has no request context,
/// so we walk the tenant list manually and build TenantDbContexts
/// directly via <see cref="DatabaseAdmin.BuildTenantConnectionString"/>.
/// </para>
///
/// <para>
/// <b>Failure isolation:</b> one tenant's prune failing (DB offline,
/// permission error, schema-drift mismatch) does not stop the sweep
/// for other tenants. Errors are logged and the next tenant proceeds.
/// </para>
/// </summary>
internal sealed class TenantAuditRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<AuditRetentionOptions> options,
    ILogger<TenantAuditRetentionService> logger,
    TimeProvider timeProvider) : BackgroundService
{
    private DateOnly _lastRunOnUtcDay = DateOnly.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.Enabled)
        {
            logger.LogInformation("TenantAuditRetention disabled via config; not scheduling.");
            return;
        }

        var ticker = TimeSpan.FromMinutes(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nowUtc = timeProvider.GetUtcNow();
                var todayUtc = DateOnly.FromDateTime(nowUtc.UtcDateTime);

                if (nowUtc.Hour == opt.RunAtUtcHour && todayUtc != _lastRunOnUtcDay)
                {
                    await PruneOnceAsync(opt, stoppingToken);
                    _lastRunOnUtcDay = todayUtc;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TenantAuditRetention tick failed; retrying next cycle.");
            }

            try
            {
                await Task.Delay(ticker, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    internal async Task PruneOnceAsync(AuditRetentionOptions opt, CancellationToken ct)
    {
        if (opt.TenantAuditLogDays <= 0)
        {
            logger.LogInformation("TenantAuditRetention skipped (TenantAuditLogDays={Days}).", opt.TenantAuditLogDays);
            return;
        }

        var cutoff = timeProvider.GetUtcNow().AddDays(-opt.TenantAuditLogDays);

        // Read the active-tenant roster + connection strings up front
        // so the master scope is short-lived. Each tenant's prune then
        // runs in its own scope so a slow / hung tenant doesn't pin the
        // master DbContext.
        List<(string Code, string ConnectionString)> targets;
        await using (var masterScope = scopeFactory.CreateAsyncScope())
        {
            var master = masterScope.ServiceProvider.GetRequiredService<EnkiMasterDbContext>();
            var dbAdmin = masterScope.ServiceProvider.GetRequiredService<DatabaseAdmin>();

            var active = await master.Tenants
                .AsNoTracking()
                .Where(t => t.Status == TenantStatus.Active)
                .Include(t => t.Databases)
                .Select(t => new
                {
                    t.Code,
                    DatabaseName = t.Databases
                        .Where(d => d.Kind == TenantDatabaseKind.Active)
                        .Select(d => d.DatabaseName)
                        .FirstOrDefault(),
                })
                .Where(x => x.DatabaseName != null)
                .ToListAsync(ct);

            targets = active
                .Select(t => (t.Code, ConnectionString: dbAdmin.BuildTenantConnectionString(t.DatabaseName!)))
                .ToList();
        }

        var totalPruned = 0;
        var tenantsSwept = 0;
        var tenantsFailed = 0;

        foreach (var (code, connectionString) in targets)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var pruned = await PruneTenantAsync(connectionString, cutoff, ct);
                totalPruned += pruned;
                tenantsSwept++;
                logger.LogInformation(
                    "TenantAuditRetention pruned {Rows} AuditLog rows from tenant {TenantCode}.",
                    pruned, code);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                tenantsFailed++;
                logger.LogWarning(ex,
                    "TenantAuditRetention failed for tenant {TenantCode}; continuing with the rest.",
                    code);
            }
        }

        logger.LogInformation(
            "TenantAuditRetention complete. Swept {TenantsSwept} tenants " +
            "({TenantsFailed} failed), pruned {TotalRows} rows total " +
            "older than {Days}d.",
            tenantsSwept, tenantsFailed, totalPruned, opt.TenantAuditLogDays);
    }

    /// <summary>
    /// One tenant's prune. Builds a TenantDbContext directly against
    /// the supplied connection string — the
    /// <c>ITenantDbContextFactory</c> path needs an HttpContext we
    /// don't have here.
    /// </summary>
    private static async Task<int> PruneTenantAsync(
        string connectionString,
        DateTimeOffset cutoff,
        CancellationToken ct)
    {
        var dbOptions = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorNumbersToAdd: null))
            .Options;

        await using var db = new TenantDbContext(dbOptions);

        return await db.AuditLogs
            .Where(a => a.ChangedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
