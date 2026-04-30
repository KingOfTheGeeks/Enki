using SDI.Enki.Infrastructure.Provisioning;
using SDI.Enki.Infrastructure.Provisioning.Models;

namespace SDI.Enki.WebApi.Tests.Fakes;

/// <summary>
/// Hand-rolled fake for <see cref="ITenantProvisioningService"/>. Avoids
/// pulling Moq/NSubstitute into the test project for a single-method seam.
///
/// Default behaviour: record the incoming request and return a canned
/// <see cref="ProvisionTenantResult"/>. Set <see cref="ThrowOnProvision"/>
/// to simulate a <see cref="TenantProvisioningException"/> failure path.
/// </summary>
internal sealed class FakeTenantProvisioningService : ITenantProvisioningService
{
    public ProvisionTenantRequest? LastRequest { get; private set; }
    public int CallCount { get; private set; }
    public TenantProvisioningException? ThrowOnProvision { get; set; }

    /// <summary>
    /// When true, the fake mirrors the real service's
    /// <c>EnsureCodeIsUniqueAsync</c> by remembering successful codes
    /// and throwing <see cref="TenantProvisioningException"/> on a
    /// second provision of the same code. Lets integration tests
    /// exercise the duplicate-code path through the full pipeline
    /// (controller → global exception handler → ProblemDetails)
    /// without spinning up real SQL Server / databases.
    /// </summary>
    public bool RejectDuplicateCodes { get; set; }

    private readonly HashSet<string> _seenCodes = new(StringComparer.OrdinalIgnoreCase);

    public Task<ProvisionTenantResult> ProvisionAsync(
        ProvisionTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastRequest = request;

        if (ThrowOnProvision is not null) throw ThrowOnProvision;

        if (RejectDuplicateCodes && !_seenCodes.Add(request.Code))
            throw new TenantProvisioningException(
                $"Tenant code '{request.Code}' already exists.");

        return Task.FromResult(new ProvisionTenantResult(
            TenantId:             Guid.NewGuid(),
            Code:                 request.Code,
            ServerInstance:       "test-server",
            ActiveDatabaseName:   $"Enki_{request.Code}_Active",
            ArchiveDatabaseName:  $"Enki_{request.Code}_Archive",
            AppliedSchemaVersion: "20260101000000_Initial",
            CompletedAt:          DateTimeOffset.UtcNow));
    }
}
