namespace SDI.Enki.Core.TenantDb.Audit;

/// <summary>
/// Immutable per-tenant change-history record. One row per
/// IAuditable entity insert / update / delete in the tenant DB.
///
/// <para>
/// Captured by <c>TenantDbContext.SaveChangesAsync</c>: every entity
/// in the change tracker that implements <c>IAuditable</c> emits an
/// <see cref="AuditLog"/> row with the entity's <see cref="EntityType"/>,
/// <see cref="EntityId"/> (the primary-key value, serialised to string),
/// the action that fired, the actor (current user or "system"),
/// the timestamp, and JSON snapshots of the property values
/// before / after the change. The audit row is enlisted into the same
/// <c>SaveChangesAsync</c> batch so capture and the underlying mutation
/// commit (or roll back) atomically.
/// </para>
///
/// <para>
/// The entity is intentionally <i>not</i> <c>IAuditable</c> itself —
/// we don't want audit-of-the-audit recursion. It's append-only:
/// the read API is the only consumer; there is no Update or Delete
/// endpoint. A future pruning policy (90-day retention, etc.) lives
/// outside this class.
/// </para>
///
/// <para>
/// <b>What's <i>not</i> here:</b> the master-DB audit (Tenant /
/// TenantUser / SystemSetting) is a separate concern. Adding
/// audit-of-master would mean either reading both DBs to assemble a
/// timeline, or duplicating master changes into every tenant DB.
/// The current pass keeps audit per-tenant — the user-visible
/// timelines are scoped to the tenant context anyway.
/// </para>
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    /// <summary>
    /// CLR class name of the audited entity (e.g. <c>Survey</c>,
    /// <c>Job</c>, <c>Well</c>). Indexed for entity-scoped lookup.
    /// </summary>
    public string EntityType { get; set; } = "";

    /// <summary>
    /// Primary-key value of the audited row, serialised to string.
    /// Always a single column today (every IAuditable in the tenant
    /// DB is keyed on a single Id property — int or Guid). Stored
    /// as string so a future composite-key entity doesn't force a
    /// schema change.
    /// </summary>
    public string EntityId { get; set; } = "";

    /// <summary>
    /// One of <c>Created</c>, <c>Updated</c>, <c>Deleted</c>. String
    /// rather than enum to keep the audit table provider-portable
    /// and to avoid coupling existing tenants to a SmartEnum value
    /// table that's only read by code.
    /// </summary>
    public string Action { get; set; } = "";

    /// <summary>
    /// JSON object of the entity's property values <i>before</i> the
    /// change. <c>null</c> for <see cref="Action"/> = "Created" (no
    /// before-state). Excludes the <c>RowVersion</c> column —
    /// concurrency tokens are operational metadata, not audit data,
    /// and including them would dump 8 bytes of base64 noise into
    /// every audit row.
    /// </summary>
    public string? OldValues { get; set; }

    /// <summary>
    /// JSON object of the entity's property values <i>after</i> the
    /// change. <c>null</c> for <see cref="Action"/> = "Deleted".
    /// Same RowVersion exclusion.
    /// </summary>
    public string? NewValues { get; set; }

    /// <summary>
    /// Pipe-delimited list of property names that actually changed
    /// on an Update (e.g. <c>Inclination|Azimuth</c>). Useful for
    /// the UI tile so it can render "fields changed: …" without
    /// diffing the JSON columns client-side. <c>null</c> for
    /// Created / Deleted (the whole entity changed).
    /// </summary>
    public string? ChangedColumns { get; set; }

    /// <summary>UTC timestamp of the SaveChangesAsync call.</summary>
    public DateTimeOffset ChangedAt { get; set; }

    /// <summary>
    /// User who initiated the change. Same source as
    /// <c>IAuditable.UpdatedBy</c> — <c>ICurrentUser.UserId</c> on
    /// HTTP requests, <c>"system"</c> for design-time tooling and
    /// background workers.
    /// </summary>
    public string ChangedBy { get; set; } = "";
}
