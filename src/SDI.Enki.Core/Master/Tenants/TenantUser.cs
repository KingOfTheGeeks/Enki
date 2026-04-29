using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Core.Master.Users;

namespace SDI.Enki.Core.Master.Tenants;

/// <summary>
/// Grants an SDI user access to a tenant with a specific role. A user with
/// zero TenantUser rows can authenticate but sees no tenants; access is
/// enforced by the tenant-routing middleware on every API request.
///
/// <para>
/// Implements <see cref="IAuditable"/> so role grants / revocations land in
/// the master audit log alongside Tenant / License changes. RowVersion
/// gates concurrent role edits with the standard 409-on-conflict pattern;
/// adding it retired the deferred tech-debt note that previously sat on
/// <c>TenantMembersController.SetRole</c>.
/// </para>
/// </summary>
public class TenantUser(Guid tenantId, Guid userId, TenantUserRole role) : IAuditable
{
    /// <summary>FK to <see cref="Tenant.Id"/>. Composite PK.</summary>
    public Guid TenantId { get; set; } = tenantId;

    /// <summary>FK to <see cref="User.Id"/>. Composite PK.</summary>
    public Guid UserId { get; set; } = userId;

    public TenantUserRole Role { get; set; } = role;

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
