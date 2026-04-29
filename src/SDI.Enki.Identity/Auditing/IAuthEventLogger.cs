using SDI.Enki.Identity.Data;

namespace SDI.Enki.Identity.Auditing;

/// <summary>
/// Writes a single <see cref="AuthEventLog"/> row from the calling
/// pipeline (Razor Page, OpenIddict authorization endpoint, etc.).
/// Resolves <c>HttpContext</c> internally so call sites don't have
/// to plumb IP / user-agent themselves — pass the event-specific
/// fields, the rest is enriched.
///
/// <para>
/// Scoped lifetime: depends on a per-request <c>ApplicationDbContext</c>
/// and <c>IHttpContextAccessor</c>. One write per call; no batching
/// (auth events fire infrequently per request and the SaveChanges
/// surface is small).
/// </para>
///
/// <para>
/// <b>Failure mode:</b> an exception while writing the audit row
/// must <i>never</i> break the user-facing auth flow. Implementation
/// catches and Serilog-warns — the auth-event log is observability,
/// not a transactional contract. If the DB is down, sign-in still
/// completes; we lose the event but don't lock the user out.
/// </para>
/// </summary>
public interface IAuthEventLogger
{
    /// <summary>
    /// Append a sign-in / sign-out / token / lockout event.
    /// </summary>
    /// <param name="eventType">
    /// One of <c>SignInSucceeded</c>, <c>SignInFailed</c>, <c>SignOut</c>,
    /// <c>TokenIssued</c>, <c>LockoutTriggered</c>. Mirrored in
    /// <see cref="AuthEventLog.EventType"/>.
    /// </param>
    /// <param name="username">
    /// Resolved or attempted username. Required — pass empty string only
    /// if the underlying flow genuinely had none (rare).
    /// </param>
    /// <param name="identityId">
    /// AspNetUsers.Id when the request resolved to a real user; null for
    /// failed sign-ins against unknown usernames.
    /// </param>
    /// <param name="detail">
    /// Optional JSON payload — failure reason, grant type. Implementation
    /// stores verbatim; serialise on the call site.
    /// </param>
    /// <param name="ct">Cancellation token from the calling pipeline.</param>
    Task LogAsync(
        string eventType,
        string username,
        string? identityId = null,
        string? detail = null,
        CancellationToken ct = default);
}
