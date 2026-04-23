using Microsoft.Data.SqlClient;
using SDI.Enki.Infrastructure.Provisioning.Internal;

namespace SDI.Enki.Infrastructure.Tests.Provisioning;

/// <summary>
/// Verifies that tenant and admin connection strings inherit every setting
/// from the master connection string (auth, encryption, pooling) and only
/// swap InitialCatalog. Security-sensitive: if auth mode leaked or changed
/// between master and tenant, we'd silently weaken or break connections.
/// </summary>
public class TenantConnectionStringBuilderTests
{
    private const string MasterConn =
        "Server=sql-prod\\SDI;Database=Enki_Master;Trusted_Connection=True;" +
        "Encrypt=True;TrustServerCertificate=True;Application Name=Enki";

    [Fact]
    public void ForTenantDatabase_SwapsInitialCatalog()
    {
        var tenantConn = TenantConnectionStringBuilder
            .ForTenantDatabase(MasterConn, "Enki_EXXON_Active");

        var parsed = new SqlConnectionStringBuilder(tenantConn);
        Assert.Equal("Enki_EXXON_Active", parsed.InitialCatalog);
        Assert.Equal("sql-prod\\SDI",      parsed.DataSource);
        Assert.True(parsed.IntegratedSecurity, "Integrated Security should inherit from master.");
        Assert.True(parsed.Encrypt,            "Encrypt should inherit from master.");
        Assert.True(parsed.TrustServerCertificate);
        Assert.Equal("Enki",               parsed.ApplicationName);
    }

    [Fact]
    public void ForServerAdminConnection_TargetsMasterCatalog()
    {
        var adminConn = TenantConnectionStringBuilder
            .ForServerAdminConnection(MasterConn);

        var parsed = new SqlConnectionStringBuilder(adminConn);
        Assert.Equal("master",             parsed.InitialCatalog);
        Assert.Equal("sql-prod\\SDI",      parsed.DataSource);
    }

    [Fact]
    public void GetServerInstance_ReturnsDataSource()
    {
        Assert.Equal("sql-prod\\SDI", TenantConnectionStringBuilder.GetServerInstance(MasterConn));
    }
}
