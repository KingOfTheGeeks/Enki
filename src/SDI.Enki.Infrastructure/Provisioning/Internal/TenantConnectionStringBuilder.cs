using Microsoft.Data.SqlClient;

namespace SDI.Enki.Infrastructure.Provisioning.Internal;

/// <summary>
/// Produces connection strings for tenant databases derived from the master
/// connection string. Inherits the master's auth / server / encryption / pool
/// settings so we don't re-specify them per tenant.
/// </summary>
internal static class TenantConnectionStringBuilder
{
    public static string ForTenantDatabase(string masterConnectionString, string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = databaseName,
        };
        return builder.ConnectionString;
    }

    public static string ForServerAdminConnection(string masterConnectionString)
    {
        // CREATE/DROP DATABASE and ALTER DATABASE ... SET READ_ONLY can't run
        // in the context of the database they target. Target "master" instead.
        var builder = new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = "master",
        };
        return builder.ConnectionString;
    }

    public static string GetServerInstance(string masterConnectionString)
    {
        return new SqlConnectionStringBuilder(masterConnectionString).DataSource;
    }
}
