using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SDI.Enki.Infrastructure.Provisioning.Internal;
using SDI.Enki.Infrastructure.Provisioning.Models;

namespace SDI.Enki.Infrastructure.Provisioning;

/// <summary>
/// Raw-SQL admin operations that must run against the <c>master</c>
/// database: CREATE / DROP DATABASE and ALTER DATABASE ... SET READ_ONLY.
/// None of these can run inside a user-database transaction, which is why
/// they're extracted from <see cref="TenantProvisioningService"/>.
/// </summary>
public sealed class DatabaseAdmin(ProvisioningOptions options, ILogger<DatabaseAdmin> logger)
{
    private string AdminConnectionString =>
        TenantConnectionStringBuilder.ForServerAdminConnection(options.MasterConnectionString);

    /// <summary>
    /// Builds a connection string pointing at a specific tenant database,
    /// inheriting auth / encryption / pool settings from the master string.
    /// </summary>
    public string BuildTenantConnectionString(string databaseName) =>
        TenantConnectionStringBuilder.ForTenantDatabase(options.MasterConnectionString, databaseName);

    public async Task CreateDatabaseIfMissingAsync(string databaseName, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(AdminConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"IF DB_ID(N'{databaseName}') IS NULL CREATE DATABASE [{databaseName}];";
        await cmd.ExecuteNonQueryAsync(ct);
        logger.LogInformation("Ensured database {Database} exists.", databaseName);
    }

    public Task SetReadOnlyAsync(string databaseName, CancellationToken ct = default) =>
        SetDbOptionAsync(databaseName, "READ_ONLY", ct);

    public Task SetReadWriteAsync(string databaseName, CancellationToken ct = default) =>
        SetDbOptionAsync(databaseName, "READ_WRITE", ct);

    private async Task SetDbOptionAsync(string databaseName, string option, CancellationToken ct)
    {
        await using var conn = new SqlConnection(AdminConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"ALTER DATABASE [{databaseName}] SET {option} WITH ROLLBACK IMMEDIATE;";
        await cmd.ExecuteNonQueryAsync(ct);
        logger.LogInformation("Set {Database} to {Option}.", databaseName, option);
    }
}
