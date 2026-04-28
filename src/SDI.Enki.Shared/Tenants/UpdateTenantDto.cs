using System.ComponentModel.DataAnnotations;

namespace SDI.Enki.Shared.Tenants;

/// <summary>
/// Mutable fields for an existing tenant. Code is not here — changing
/// a tenant code would cascade through database names and foreign keys;
/// it's effectively a new tenant. Status is managed via the separate
/// /deactivate and /reactivate endpoints on <c>TenantsController</c>.
/// Region is not on tenants; it belongs on Jobs.
/// </summary>
public sealed record UpdateTenantDto(
    [Required, MaxLength(200)] string Name,
    [MaxLength(200)] string? DisplayName = null,
    [MaxLength(256), EmailAddress] string? ContactEmail = null,
    string? Notes = null,

    [Required(ErrorMessage = "RowVersion is required for optimistic concurrency.")]
    string? RowVersion = null);
