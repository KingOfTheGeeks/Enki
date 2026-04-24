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

    public Task<ProvisionTenantResult> ProvisionAsync(
        ProvisionTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastRequest = request;

        if (ThrowOnProvision is not null) throw ThrowOnProvision;

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
