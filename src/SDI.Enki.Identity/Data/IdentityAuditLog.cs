namespace SDI.Enki.Identity.Data;

/// <summary>
/// Append-only change history for sensitive Identity-DB actions —
/// admin-role flips, password resets, lockouts. Same shape as the
/// tenant-side <c>SDI.Enki.Core.TenantDb.Audit.AuditLog</c> and the
/// master-side <c>SDI.Enki.Core.Master.Audit.MasterAuditLog</c> so
/// the read DTO stays shared.
///
/// <para>
/// <b>Why a third audit table:</b> the Identity DB is its own
/// deployment (separate connection, separate schema, separate
/// host). ASP.NET Identity entities don't implement
/// <c>IAuditable</c> and ApplicationUser writes happen through
/// <c>UserManager</c> rather than the DbContext directly — so the
/// auto-capture pattern that works on master + tenant doesn't
/// drop in cleanly here. Manual writes from the AdminUsers
/// controller are the simplest fit; this entity exists purely as
/// the storage shape for those writes.
/// </para>
///
/// <para>
/// Lives in the Identity host project (not Core / Shared) because
/// every consumer is internal to this host: the
/// <c>AdminUsersController</c> writes rows, an <c>IdentityAudit</c>
/// read endpoint serves them. WebApi never touches this table —
/// keeping it host-local preserves the Identity-only deployment
/// boundary.
/// </para>
/// </summary>
public class IdentityAuditLog
{
    public long Id { get; set; }

    /// <summary>CLR class name (e.g. <c>ApplicationUser</c>).</summary>
    public string EntityType { get; set; } = "";

    /// <summary>Primary-key value of the audited row (the AspNetUsers Id).</summary>
    public string EntityId { get; set; } = "";

    /// <summary>
    /// One of <c>RoleGranted</c>, <c>RoleRevoked</c>, <c>PasswordReset</c>,
    /// <c>Locked</c>, <c>Unlocked</c>. Free-text rather than enum so the
    /// table doesn't grow a SmartEnum value table that's only ever
    /// consumed by reads.
    /// </summary>
    public string Action { get; set; } = "";

    /// <summary>JSON before-snapshot. Optional — sparse for ApplicationUser.</summary>
    public string? OldValues { get; set; }

    /// <summary>JSON after-snapshot. Optional.</summary>
    public string? NewValues { get; set; }

    /// <summary>Pipe-delimited modified property names. Null when N/A.</summary>
    public string? ChangedColumns { get; set; }

    /// <summary>UTC timestamp.</summary>
    public DateTimeOffset ChangedAt { get; set; }

    /// <summary>
    /// AspNetUsers.Id of the admin who initiated the action (taken from
    /// <c>UserManager.GetUserId(User)</c>). Falls back to <c>"system"</c>
    /// only for non-HTTP code paths — admin actions always have a
    /// principal.
    /// </summary>
    public string ChangedBy { get; set; } = "";
}
