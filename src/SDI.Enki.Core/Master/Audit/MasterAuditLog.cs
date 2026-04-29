namespace SDI.Enki.Core.Master.Audit;

/// <summary>
/// Immutable change-history record for the master DB. One row per
/// IAuditable insert / update / delete on the master context — the
/// twin of <c>SDI.Enki.Core.TenantDb.Audit.AuditLog</c> on the tenant
/// side.
///
/// <para>
/// Captured by <c>EnkiMasterDbContext.SaveChangesAsync</c>: every
/// entity in the change tracker that implements <c>IAuditable</c>
/// emits a row with the entity's CLR type, primary-key string,
/// action, actor, timestamp, and JSON snapshots before / after.
/// The audit row enlists in the same <c>SaveChangesAsync</c> batch
/// so capture and the underlying mutation commit (or roll back)
/// atomically.
/// </para>
///
/// <para>
/// <b>Why a separate table from the tenant audit:</b> the master DB
/// is a single deployment-wide store; the tenant DBs are per-customer.
/// Putting master events in one of the tenant DBs would silo them
/// arbitrarily. Putting them in a third DB would mean three audit
/// surfaces. Co-locating with the entities they describe — same DB
/// as Tenant / TenantUser / License / Tool — keeps reads simple and
/// commits atomic. The shape mirrors <c>AuditLog</c> deliberately so
/// the read DTOs can be shared.
/// </para>
///
/// <para>
/// The entity is intentionally <i>not</i> <c>IAuditable</c> — no
/// audit-of-the-audit recursion. Append-only: read API only; no
/// Update or Delete endpoint.
/// </para>
/// </summary>
public class MasterAuditLog
{
    public long Id { get; set; }

    /// <summary>CLR class name (e.g. <c>Tenant</c>, <c>TenantUser</c>, <c>License</c>).</summary>
    public string EntityType { get; set; } = "";

    /// <summary>
    /// Primary-key value, serialised to string. Composite PKs (TenantUser
    /// is keyed on TenantId+UserId) are pipe-joined: <c>{tenantId}|{userId}</c>.
    /// </summary>
    public string EntityId { get; set; } = "";

    /// <summary>One of <c>Created</c>, <c>Updated</c>, <c>Deleted</c>.</summary>
    public string Action { get; set; } = "";

    /// <summary>JSON before-snapshot. Null for Created. Excludes RowVersion.</summary>
    public string? OldValues { get; set; }

    /// <summary>JSON after-snapshot. Null for Deleted. Excludes RowVersion.</summary>
    public string? NewValues { get; set; }

    /// <summary>Pipe-delimited modified property names on Update. Null for Created/Deleted.</summary>
    public string? ChangedColumns { get; set; }

    /// <summary>UTC timestamp of the SaveChangesAsync call.</summary>
    public DateTimeOffset ChangedAt { get; set; }

    /// <summary><c>ICurrentUser.UserId</c> on HTTP requests, <c>"system"</c> otherwise.</summary>
    public string ChangedBy { get; set; } = "";
}
