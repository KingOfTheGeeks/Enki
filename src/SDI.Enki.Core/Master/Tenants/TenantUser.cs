using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.Master.Users;

namespace SDI.Enki.Core.Master.Tenants;

/// <summary>
/// Grants an SDI Team user access to a tenant. A user with zero
/// TenantUser rows can authenticate but sees no tenants; access is
/// enforced by the tenant-routing middleware on every API request.
///
/// <para>
/// <b>Role retired (2026-05-01).</b> The previous <c>Role</c> column
/// (<c>TenantUserRole</c> SmartEnum: Admin / Contributor / Viewer)
/// only ever gated tenant-member-management; that gate has moved to
/// the system-wide <c>TeamSubtype</c> hierarchy (Supervisor+ now
/// manages members). Tenant-internal admin within a customer org
/// becomes a Tenant-portal concept later — not a column on this row.
/// </para>
///
/// <para>
/// Implements <see cref="IAuditable"/> so grants / revocations land in
/// the master audit log alongside Tenant / License changes. RowVersion
/// is preserved on the row even though the only mutation today is
/// remove (Add never updates an existing row); leaving it as defence
/// against future fields needing concurrency control.
/// </para>
/// </summary>
public class TenantUser(Guid tenantId, Guid userId) : IAuditable
{
    /// <summary>FK to <see cref="Tenant.Id"/>. Composite PK.</summary>
    public Guid TenantId { get; set; } = tenantId;

    /// <summary>FK to <see cref="User.Id"/>. Composite PK.</summary>
    public Guid UserId { get; set; } = userId;

    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Optional audit: which admin granted this access.</summary>
    public Guid? GrantedBy { get; set; }

    // IAuditable — populated by EnkiMasterDbContext.SaveChangesAsync.
    public DateTimeOffset  CreatedAt  { get; set; }
    public string?         CreatedBy  { get; set; }
    public DateTimeOffset? UpdatedAt  { get; set; }
    public string?         UpdatedBy  { get; set; }
    public byte[]?         RowVersion { get; set; }

    // EF navs
    public Tenant? Tenant { get; set; }
    public User? User { get; set; }
}
