using SDI.Enki.Core.Master.Tenants.Enums;
using SDI.Enki.Core.Master.Users;

namespace SDI.Enki.Core.Master.Tenants;

/// <summary>
/// Grants an SDI user access to a tenant with a specific role. A user with
/// zero TenantUser rows can authenticate but sees no tenants; access is
/// enforced by the tenant-routing middleware on every API request.
/// </summary>
public class TenantUser(Guid tenantId, Guid userId, TenantUserRole role)
{
    /// <summary>FK to <see cref="Tenant.Id"/>. Composite PK.</summary>
    public Guid TenantId { get; set; } = tenantId;

    /// <summary>FK to <see cref="User.Id"/>. Composite PK.</summary>
    public Guid UserId { get; set; } = userId;

    public TenantUserRole Role { get; set; } = role;

    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Optional audit: which admin granted this access.</summary>
    public Guid? GrantedBy { get; set; }

    // EF navs
    public Tenant? Tenant { get; set; }
    public User? User { get; set; }
}
