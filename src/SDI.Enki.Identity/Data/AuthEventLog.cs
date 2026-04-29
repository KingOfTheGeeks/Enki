namespace SDI.Enki.Identity.Data;

/// <summary>
/// Append-only event log for authentication-side activity — sign-in
/// attempts (success + failure), sign-outs, token issuance, account
/// lockouts. Distinct from <see cref="IdentityAuditLog"/> by shape:
/// audit log is "admin did X to user Y"; this is "user attempted
/// authentication, here is the outcome." Different fields, different
/// query patterns, different retention pressures.
///
/// <para>
/// <b>Retention:</b> rows are append-only; a 90-day prune is the
/// recommended retention policy (sign-in volume can be high in a
/// brute-force attack, and the security-relevant signal is the
/// recent window). The pruner is out of scope here — when it lands,
/// it sweeps <c>OccurredAt &lt; UtcNow.AddDays(-90)</c>. Until then
/// the table grows unbounded; rate-limiting on <c>/connect/*</c>
/// caps the realistic write rate at 10/min/IP.
/// </para>
///
/// <para>
/// <b>Failed sign-ins for unknown usernames</b> are logged with
/// <see cref="Username"/> set to the attempted-username string and
/// <see cref="IdentityId"/> null. This is intentional: brute-force /
/// credential-stuffing fishing for valid usernames is a real
/// threat model and the log needs to surface it. We do <i>not</i>
/// log the attempted password.
/// </para>
///
/// <para>
/// <b>PII:</b> <see cref="IpAddress"/> and <see cref="UserAgent"/>
/// are personal data. Retention policy above limits the window;
/// admin read access is gated by <c>EnkiAdmin</c>.
/// </para>
/// </summary>
public class AuthEventLog
{
    public long Id { get; set; }

    /// <summary>
    /// One of <c>SignInSucceeded</c>, <c>SignInFailed</c>,
    /// <c>SignOut</c>, <c>TokenIssued</c>, <c>LockoutTriggered</c>.
    /// String rather than SmartEnum to keep the table provider-portable
    /// and to avoid coupling the schema to a value table that's only
    /// read by code.
    /// </summary>
    public string EventType { get; set; } = "";

    /// <summary>
    /// The username the request claimed to be. For successful sign-ins
    /// + sign-outs this is the resolved <c>UserName</c>; for failed
    /// sign-ins against an unknown user it is the raw attempted
    /// string (so credential-stuffing fishing is visible). Capped at
    /// 256 to bound storage; longer attempts get truncated.
    /// </summary>
    public string Username { get; set; } = "";

    /// <summary>
    /// AspNetUsers.Id when the request resolved to a real user; null
    /// for failed sign-ins against unknown usernames.
    /// </summary>
    public string? IdentityId { get; set; }

    /// <summary>
    /// Caller IP from <c>HttpContext.Connection.RemoteIpAddress</c>.
    /// When deployed behind a reverse proxy, ForwardedHeaders
    /// middleware should already be hydrating this from
    /// <c>X-Forwarded-For</c>; otherwise this is the proxy IP.
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>User-Agent header verbatim (truncated to 500 chars).</summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Optional event-specific JSON payload. For
    /// <c>SignInFailed</c> this is <c>{"reason":"BadPassword"}</c> /
    /// <c>"LockedOut"</c> / <c>"NotAllowed"</c> / <c>"TwoFactorRequired"</c>;
    /// for <c>TokenIssued</c> it's <c>{"grantType":"authorization_code"}</c>
    /// or <c>"refresh_token"</c>; null for events that have no extra
    /// shape.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>UTC timestamp.</summary>
    public DateTimeOffset OccurredAt { get; set; }
}
