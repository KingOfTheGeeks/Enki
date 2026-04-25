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
/// Host-level "is this a dev environment" flag. Gates whether
/// <c>DevMasterSeeder</c> runs at all on startup — when true, the
/// canonical bootstrap tenant (TENANTTEST) is auto-provisioned with
/// demo Jobs so dev click-throughs land on real content. WebApi turns
/// this on via <c>builder.Environment.IsDevelopment()</c>; Migrator
/// CLI and production hosts leave it false.
///
/// <para>
/// <b>Does NOT control per-tenant seed-on-provision.</b> Whether a
/// specific provision call seeds demo data is decided per-call via
/// <see cref="ProvisionTenantRequest.SeedSampleData"/>. User-created
/// tenants from the UI always come up empty regardless of this flag.
/// </para>
/// </param>
public sealed record ProvisioningOptions(
    string MasterConnectionString,
    bool SeedSampleData = false);
