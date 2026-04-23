namespace SDI.Enki.Infrastructure.Provisioning.Models;

/// <summary>
/// Configuration for the tenant provisioning + migration pipeline. Separated
/// from IConfiguration so services receive a strongly-typed value and the
/// host decides exactly how to source it (appsettings, env vars, Key Vault).
/// </summary>
public sealed record ProvisioningOptions(string MasterConnectionString);
