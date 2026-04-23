namespace SDI.Enki.Infrastructure.Provisioning.Models;

/// <summary>
/// Outcome of a provisioning attempt. Returned on success; failures throw
/// <see cref="TenantProvisioningException"/>.
/// </summary>
public sealed record ProvisionTenantResult(
    Guid TenantId,
    string Code,
    string ServerInstance,
    string ActiveDatabaseName,
    string ArchiveDatabaseName,
    string AppliedSchemaVersion,
    DateTimeOffset CompletedAt);
