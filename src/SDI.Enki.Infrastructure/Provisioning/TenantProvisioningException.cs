namespace SDI.Enki.Infrastructure.Provisioning;

/// <summary>
/// Thrown when tenant provisioning fails. Carries the partially-provisioned
/// TenantId (if one was persisted) so callers / admin UIs can trigger cleanup.
/// </summary>
public sealed class TenantProvisioningException(string message, Guid? partialTenantId = null, Exception? inner = null)
    : Exception(message, inner)
{
    public Guid? PartialTenantId { get; } = partialTenantId;
}
