namespace SDI.Enki.Identity.Background;

/// <summary>
/// Configuration for the Identity-host audit-retention sweep. Bound
/// from the <c>AuditRetention</c> section of appsettings; every key
/// has a sensible default so a missing config doesn't disable the
/// service silently.
///
/// <para>
/// Set any <c>*Days</c> value to <c>0</c> (or negative) to disable
/// pruning of that table — e.g. for a tenant with regulatory
/// requirements demanding indefinite retention.
/// </para>
/// </summary>
public sealed class AuditRetentionOptions
{
    public const string SectionName = "AuditRetention";

    /// <summary>Master kill-switch. Default true. Set false in dev to silence the daily prune log line.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>UTC hour-of-day to run the sweep. Default 03:00 — typical low-traffic window.</summary>
    public int RunAtUtcHour { get; set; } = 3;

    /// <summary>Days to keep <c>AuthEventLog</c> rows. Default 90 — high-volume + PII-laden.</summary>
    public int AuthEventLogDays { get; set; } = 90;

    /// <summary>Days to keep <c>IdentityAuditLog</c> rows. Default 365 — admin actions, low volume.</summary>
    public int IdentityAuditLogDays { get; set; } = 365;
}
