using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.WebApi.Background;

/// <summary>
/// Daily sweep that prunes old <c>MasterAuditLog</c> rows from the
/// master DB. Twin of the Identity-host retention service; same
/// schedule shape (one-minute wake cycle, fires when the configured
/// UTC hour matches and the day has rolled).
///
/// <para>
/// MasterAuditLog churn is dominated by tenant-management activity
/// (member add/remove, role flips) and AuthzDenials. Default 365-day
/// retention is conservative — adjust per compliance posture.
/// </para>
///
/// <para>
/// <b>Failure mode:</b> swallow + log. A bad tick should not crash
/// the host or mask tomorrow's prune.
/// </para>
/// </summary>
internal sealed class MasterAuditRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<AuditRetentionOptions> options,
    ILogger<MasterAuditRetentionService> logger,
    TimeProvider timeProvider) : BackgroundService
{
    private DateOnly _lastRunOnUtcDay = DateOnly.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.Enabled)
        {
            logger.LogInformation("MasterAuditRetention disabled via config; not scheduling.");
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
                logger.LogWarning(ex, "MasterAuditRetention tick failed; retrying next cycle.");
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
        if (opt.MasterAuditLogDays <= 0)
        {
            logger.LogInformation("MasterAuditRetention skipped (MasterAuditLogDays={Days}).", opt.MasterAuditLogDays);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EnkiMasterDbContext>();

        var cutoff = CutoffFor(timeProvider.GetUtcNow(), opt.MasterAuditLogDays);
        var pruned = await db.MasterAuditLogs
            .Where(a => a.ChangedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        logger.LogInformation(
            "MasterAuditRetention pruned {Rows} MasterAuditLog rows older than {Days}d.",
            pruned, opt.MasterAuditLogDays);
    }

    /// <summary>
    /// Cutoff timestamp for a given retention window. Pure function —
    /// exposed for tests that verify the policy math without invoking
    /// EF's <c>ExecuteDeleteAsync</c> (which the InMemory provider
    /// doesn't support; SQL Server does).
    /// </summary>
    internal static DateTimeOffset CutoffFor(DateTimeOffset now, int days) =>
        now.AddDays(-days);
}
