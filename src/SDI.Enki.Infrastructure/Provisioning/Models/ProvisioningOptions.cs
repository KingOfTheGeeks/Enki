namespace SDI.Enki.Infrastructure.Provisioning.Models;

/// <summary>
/// Configuration for the tenant provisioning + migration pipeline. Separated
/// from IConfiguration so services receive a strongly-typed value and the
/// host decides exactly how to source it (appsettings, env vars, Key Vault).
/// </summary>
/// <param name="MasterConnectionString">
/// Connection string to the Enki master DB. Per-tenant connection strings
/// are built from this at request time (same server + creds, different
/// database name).
/// </param>
/// <param name="SeedSampleData">
/// When true, newly provisioned tenants receive a curated set of sample
/// Jobs in their Active DB so developers clicking through the UI land on
/// real content instead of an empty grid. WebApi turns this on via
/// <c>builder.Environment.IsDevelopment()</c>; Migrator CLI and production
/// hosts leave it false. See <c>DevTenantSeeder</c>.
/// </param>
public sealed record ProvisioningOptions(
    string MasterConnectionString,
    bool SeedSampleData = false);
