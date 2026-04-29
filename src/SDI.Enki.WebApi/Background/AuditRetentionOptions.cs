namespace SDI.Enki.WebApi.Background;

/// <summary>
/// Configuration for the WebApi-host audit-retention sweep. Two
/// pruners use this options shape — <see cref="MasterAuditRetentionService"/>
/// for the master DB and <see cref="TenantAuditRetentionService"/> for
/// the per-tenant fan-out. Bound from the <c>AuditRetention</c> section
/// of appsettings.
///
/// <para>
/// Set any <c>*Days</c> value to <c>0</c> (or negative) to disable
/// pruning of that table — useful when a regulatory window demands
/// indefinite retention.
/// </para>
/// </summary>
public sealed class AuditRetentionOptions
{
    public const string SectionName = "AuditRetention";

    /// <summary>Master kill-switch. Default true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>UTC hour-of-day to run the sweep. Default 03:00.</summary>
    public int RunAtUtcHour { get; set; } = 3;

    /// <summary>Days to keep <c>MasterAuditLog</c> rows. Default 365 — cross-tenant ops events, low-medium volume.</summary>
    public int MasterAuditLogDays { get; set; } = 365;

    /// <summary>Days to keep per-tenant <c>AuditLog</c> rows. Default 730 — drilling ops history holds business value longer.</summary>
    public int TenantAuditLogDays { get; set; } = 730;
}
