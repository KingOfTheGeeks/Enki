using SDI.Enki.Core.Abstractions;
using SDI.Enki.Core.Master.Tenants.Enums;

namespace SDI.Enki.Core.Master.Tenants;

/// <summary>
/// A client/operator whose data lives in a dedicated pair of tenant databases.
/// Every `Job` in the system belongs to exactly one tenant.
///
/// Implements <see cref="IAuditable"/> — CreatedBy / UpdatedBy /
/// RowVersion are managed by <c>EnkiMasterDbContext.SaveChangesAsync</c>;
/// don't set them from business code.
/// </summary>
public class Tenant(string code, string name) : IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Short, stable handle — e.g. "EXXON", "CVX". Uppercase, underscore-free.</summary>
    public string Code { get; set; } = code;

    /// <summary>Canonical legal name — e.g. "ExxonMobil Production Company".</summary>
    public string Name { get; set; } = name;

    /// <summary>UI-friendly override. Falls back to Name when null.</summary>
    public string? DisplayName { get; set; }

    public TenantStatus Status { get; set; } = TenantStatus.Active;

    public string? ContactEmail { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset? DeactivatedAt { get; set; }

    // IAuditable — managed by the DbContext interceptor; treat as read-only.
    public DateTimeOffset   CreatedAt  { get; set; } = DateTimeOffset.UtcNow;
    public string?          CreatedBy  { get; set; }
    public DateTimeOffset?  UpdatedAt  { get; set; }
    public string?          UpdatedBy  { get; set; }
    public byte[]?          RowVersion { get; set; }

    // EF navigations
    public ICollection<TenantDatabase> Databases { get; set; } = new List<TenantDatabase>();
    public ICollection<TenantUser> Users { get; set; } = new List<TenantUser>();
}
