using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.Infrastructure.DesignTime;

/// <summary>
/// Enables <c>dotnet ef migrations add</c> and <c>database update</c>
/// against <see cref="EnkiMasterDbContext"/> without booting a host.
///
/// <para>
/// <b>No fallback connection string.</b> Earlier revisions hardcoded a
/// dev <c>sa</c> credential here, which leaked through any artifact
/// inspection of the compiled assembly. Set the
/// <c>EnkiMasterCs</c> environment variable before invoking
/// <c>dotnet ef</c>. Per-shell example (PowerShell):
/// <code>$env:EnkiMasterCs = 'Server=localhost;Database=Enki_Master;User Id=sa;Password=...;TrustServerCertificate=True;'</code>
/// </para>
/// </summary>
public class EnkiMasterDbContextFactory : IDesignTimeDbContextFactory<EnkiMasterDbContext>
{
    public EnkiMasterDbContext CreateDbContext(string[] args)
    {
        var connectionString = ConnectionStrings.RequireMaster();

        var options = new DbContextOptionsBuilder<EnkiMasterDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(EnkiMasterDbContext).Assembly.FullName))
            .Options;

        return new EnkiMasterDbContext(options);
    }
}
