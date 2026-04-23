using SDI.Enki.Core.Master.Tenants.Enums;

namespace SDI.Enki.Core.Master.Tenants;

/// <summary>
/// A client/operator whose data lives in a dedicated pair of tenant databases.
/// Every `Job` in the system belongs to exactly one tenant.
/// </summary>
public class Tenant(string code, string name)
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Short, stable handle — e.g. "EXXON", "CVX". Uppercase, underscore-free.</summary>
    public string Code { get; set; } = code;

    /// <summary>Canonical legal name — e.g. "ExxonMobil Production Company".</summary>
    public string Name { get; set; } = name;

    /// <summary>UI-friendly override. Falls back to Name when null.</summary>
    public string? DisplayName { get; set; }

    public TenantStatus Status { get; set; } = TenantStatus.Active;

    public string? Region { get; set; }
    public string? ContactEmail { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }

    // EF navigations
    public ICollection<TenantDatabase> Databases { get; set; } = new List<TenantDatabase>();
    public ICollection<TenantUser> Users { get; set; } = new List<TenantUser>();
}
