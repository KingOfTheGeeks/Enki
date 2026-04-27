using System.Text.RegularExpressions;
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
///
/// <para>
/// SQL Server does not parameterise DDL identifiers, so the database
/// name has to be inlined into the command text. Every method here
/// validates the name against
/// <see cref="ValidateDatabaseName(string)"/> before executing —
/// defense-in-two so a future caller that skips
/// <see cref="DatabaseNaming.ValidateCode(string)"/> upstream still
/// can't reach the admin connection with an arbitrary identifier.
/// </para>
/// </summary>
public sealed partial class DatabaseAdmin(ProvisioningOptions options, ILogger<DatabaseAdmin> logger)
{
    /// <summary>
    /// Pattern for a database name produced by
    /// <see cref="DatabaseNaming.ForKind"/>: literal <c>Enki_</c>
    /// prefix, then a tenant code matching the upstream regex
    /// (<c>[A-Z][A-Z0-9_]{0,23}</c>), then the <c>Active</c> /
    /// <c>Archive</c> suffix. Source-generated for AOT-friendliness.
    /// </summary>
    [GeneratedRegex(@"^Enki_[A-Z][A-Z0-9_]{0,23}_(Active|Archive)$")]
    private static partial Regex DatabaseNamePattern();

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
        ValidateDatabaseName(databaseName);

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
        ValidateDatabaseName(databaseName);

        await using var conn = new SqlConnection(AdminConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"ALTER DATABASE [{databaseName}] SET {option} WITH ROLLBACK IMMEDIATE;";
        await cmd.ExecuteNonQueryAsync(ct);
        logger.LogInformation("Set {Database} to {Option}.", databaseName, option);
    }

    /// <summary>
    /// Reject any database name that doesn't match
    /// <see cref="DatabaseNamePattern"/>. The upstream caller
    /// (<see cref="TenantProvisioningService"/>) already validates
    /// the tenant code, but this layer doesn't trust the call site —
    /// every DDL statement emitted from this class touches the
    /// admin connection on the <c>master</c> database, so the cost
    /// of a bypassed validator is the worst possible blast radius.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="databaseName"/> is null, empty, or
    /// doesn't match the expected <c>Enki_{CODE}_{Active|Archive}</c>
    /// shape.
    /// </exception>
    internal static void ValidateDatabaseName(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("Database name must be non-empty.", nameof(databaseName));

        if (!DatabaseNamePattern().IsMatch(databaseName))
            throw new ArgumentException(
                $"Database name '{databaseName}' is invalid. Must match the " +
                "Enki_{CODE}_{Active|Archive} pattern (CODE = 1–24 chars, " +
                "uppercase A–Z / 0–9 / underscore, not starting with a digit).",
                nameof(databaseName));
    }
}
