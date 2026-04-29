using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SDI.Enki.Identity.Data;

namespace SDI.Enki.Identity.Background;

/// <summary>
/// Daily sweep that prunes old <see cref="AuthEventLog"/> +
/// <see cref="IdentityAuditLog"/> rows from the Identity DB. Without
/// this both tables grow unbounded — the AuthEventLog in particular
/// can churn fast if a brute-force attempt slips past the rate
/// limiter (10/min/IP × N IPs × hours).
///
/// <para>
/// <b>Schedule:</b> wakes once per minute, fires the prune when the
/// configured UTC hour matches and the day rolls over. A simple
/// cron-style trigger is overkill here — once-daily pruning is
/// time-of-day, not minute-precise.
/// </para>
///
/// <para>
/// <b>Implementation:</b> uses EF Core's <c>ExecuteDeleteAsync</c>
/// (set-based delete; one round-trip per table, no entity tracking).
/// The pruner runs as <see cref="IServiceScopeFactory"/>-resolved
/// scope per tick so the long-lived BackgroundService doesn't pin
/// a single DbContext for the host lifetime.
/// </para>
///
/// <para>
/// <b>Failure mode:</b> a transient SQL error is logged + swallowed.
/// The next day's tick retries. We don't crash the host on a prune
/// failure — that would cycle every Identity pod overnight on a
/// single bad SQL connection.
/// </para>
/// </summary>
internal sealed class AuditRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<AuditRetentionOptions> options,
    ILogger<AuditRetentionService> logger,
    TimeProvider timeProvider) : BackgroundService
{
    private DateOnly _lastRunOnUtcDay = DateOnly.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.Enabled)
        {
            logger.LogInformation("AuditRetention disabled via config; not scheduling.");
            return;
        }

        // One-minute wake cycle is plenty — we only need to notice the
        // configured hour rolling over once per day.
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
                logger.LogWarning(ex, "AuditRetention tick failed; retrying next cycle.");
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

    /// <summary>
    /// Internal hook for tests — one shot, deterministic, no scheduling.
    /// </summary>
    internal async Task PruneOnceAsync(AuditRetentionOptions opt, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var nowUtc = timeProvider.GetUtcNow();

        var authPruned = opt.AuthEventLogDays > 0
            ? await db.AuthEventLogs
                .Where(e => e.OccurredAt < nowUtc.AddDays(-opt.AuthEventLogDays))
                .ExecuteDeleteAsync(ct)
            : 0;

        var identityPruned = opt.IdentityAuditLogDays > 0
            ? await db.IdentityAuditLogs
                .Where(a => a.ChangedAt < nowUtc.AddDays(-opt.IdentityAuditLogDays))
                .ExecuteDeleteAsync(ct)
            : 0;

        logger.LogInformation(
            "AuditRetention pruned {AuthEventLogRows} AuthEventLog rows (>{AuthDays}d) and " +
            "{IdentityAuditLogRows} IdentityAuditLog rows (>{IdentityDays}d).",
            authPruned, opt.AuthEventLogDays,
            identityPruned, opt.IdentityAuditLogDays);
    }

    // ---------- testable extracts ----------

    /// <summary>
    /// Cutoff timestamp for a given retention window. Pure function —
    /// exposed for tests that verify the policy math without invoking
    /// EF's <c>ExecuteDeleteAsync</c> (which the InMemory provider
    /// doesn't support; SQL Server does).
    /// </summary>
    internal static DateTimeOffset CutoffFor(DateTimeOffset now, int days) =>
        now.AddDays(-days);
}
