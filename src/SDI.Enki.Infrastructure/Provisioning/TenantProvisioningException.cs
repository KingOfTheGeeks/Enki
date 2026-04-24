using SDI.Enki.Shared.Exceptions;

namespace SDI.Enki.Infrastructure.Provisioning;

/// <summary>
/// Thrown when tenant provisioning fails. Carries the partially-provisioned
/// TenantId (if one was persisted) so callers / admin UIs can trigger cleanup.
///
/// Inherits <see cref="EnkiException"/>; the WebApi's global exception
/// handler maps it to HTTP 400 with <c>partialTenantId</c> surfaced as a
/// ProblemDetails extension.
/// </summary>
public sealed class TenantProvisioningException : EnkiException
{
    public TenantProvisioningException(string message, Guid? partialTenantId = null, Exception? inner = null)
        : base(message, inner)
    {
        PartialTenantId = partialTenantId;
    }

    public Guid? PartialTenantId { get; }

    public override int HttpStatusCode => 400;
    public override string ProblemType => "https://enki.sdi/problems/provisioning-failed";

    public override IReadOnlyDictionary<string, object?> Extensions =>
        PartialTenantId is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?> { ["partialTenantId"] = PartialTenantId };
}
