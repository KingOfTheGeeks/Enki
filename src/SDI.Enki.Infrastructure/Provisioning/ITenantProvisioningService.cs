using SDI.Enki.Infrastructure.Provisioning.Models;

namespace SDI.Enki.Infrastructure.Provisioning;

/// <summary>
/// End-to-end provisioning of a new tenant: creates the master-DB rows,
/// provisions physical Active + Archive databases, applies the tenant
/// migration to both, flips Archive to <c>READ_ONLY</c>, and writes the
/// MigrationRun audit trail.
/// </summary>
public interface ITenantProvisioningService
{
    /// <summary>
    /// Provisions a new tenant.
    /// </summary>
    /// <exception cref="TenantProvisioningException">
    /// Thrown with <see cref="TenantProvisioningException.PartialTenantId"/>
    /// set when the master-DB row has been written but a downstream step
    /// failed. Callers may use that id to trigger cleanup.
    /// </exception>
    Task<ProvisionTenantResult> ProvisionAsync(
        ProvisionTenantRequest request,
        CancellationToken cancellationToken = default);
}
