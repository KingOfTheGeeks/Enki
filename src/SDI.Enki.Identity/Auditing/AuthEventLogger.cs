using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SDI.Enki.Identity.Data;

namespace SDI.Enki.Identity.Auditing;

/// <summary>
/// Default <see cref="IAuthEventLogger"/> — writes one row per call
/// to <c>ApplicationDbContext.AuthEventLogs</c>. Enriches with IP +
/// user-agent from <c>HttpContext</c> so call sites stay terse.
///
/// <para>
/// <b>Why try/catch on SaveChangesAsync:</b> an audit-write failure
/// must not abort the auth flow. If the DB is unavailable on a
/// sign-in we still want the user to log in — losing the event
/// row is worse than no audit at all, but it's still strictly
/// less bad than locking everyone out of the system.
/// </para>
/// </summary>
internal sealed class AuthEventLogger(
    ApplicationDbContext db,
    IHttpContextAccessor httpAccessor,
    ILogger<AuthEventLogger> logger) : IAuthEventLogger
{
    public async Task LogAsync(
        string eventType,
        string username,
        string? identityId = null,
        string? detail = null,
        CancellationToken ct = default)
    {
        var ctx = httpAccessor.HttpContext;

        var entry = new AuthEventLog
        {
            EventType  = eventType,
            Username   = Truncate(username, 256) ?? "",
            IdentityId = identityId,
            IpAddress  = ctx?.Connection.RemoteIpAddress?.ToString(),
            UserAgent  = Truncate(ctx?.Request.Headers.UserAgent.ToString(), 500),
            Detail     = detail,
            OccurredAt = DateTimeOffset.UtcNow,
        };

        try
        {
            db.AuthEventLogs.Add(entry);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Never propagate. We've lost the event row; that's
            // observability cost, not a blocker for the user.
            logger.LogWarning(ex,
                "Failed to write AuthEventLog row {EventType} for {Username}; continuing.",
                eventType, username);
        }
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max
            ? value
            : value[..max];
}
