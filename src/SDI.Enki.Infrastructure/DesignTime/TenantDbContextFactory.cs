using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.Infrastructure.DesignTime;

/// <summary>
/// Enables `dotnet ef migrations add` and `dotnet ef database update` against
/// <see cref="TenantDbContext"/> without needing a running host application.
/// The connection string is a placeholder — at runtime, each tenant's real
/// connection string is produced by the tenant resolver from the
/// TenantDatabase table in the master DB.
/// </summary>
public class TenantDbContextFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlServer(
                // Design-time placeholder. Replace YOUR_USER / YOUR_PASSWORD
                // with your dev credentials if invoking `dotnet ef database
                // update` without --connection.
                "Server=10.1.7.50;Database=Enki_Master;User Id=sa;Password=!@m@nAdm1n1str@t0r;TrustServerCertificate=True;Encrypt=True;",
                sql => sql.MigrationsAssembly(typeof(TenantDbContext).Assembly.FullName))
            .Options;

        return new TenantDbContext(options);
    }
}
