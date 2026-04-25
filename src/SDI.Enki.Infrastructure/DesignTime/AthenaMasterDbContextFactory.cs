using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.Infrastructure.DesignTime;

/// <summary>
/// Enables <c>dotnet ef migrations add</c> and <c>database update</c>
/// against <see cref="AthenaMasterDbContext"/> without booting a host.
///
/// <para>
/// <b>No fallback connection string.</b> Earlier revisions hardcoded a
/// dev <c>sa</c> credential here, which leaked through any artifact
/// inspection of the compiled assembly. Set the
/// <c>EnkiMasterCs</c> environment variable before invoking
/// <c>dotnet ef</c>. Per-shell example (PowerShell):
/// <code>$env:EnkiMasterCs = 'Server=10.1.7.50;Database=Enki_Master;User Id=...;Password=...;TrustServerCertificate=True;'</code>
/// </para>
/// </summary>
public class AthenaMasterDbContextFactory : IDesignTimeDbContextFactory<AthenaMasterDbContext>
{
    public AthenaMasterDbContext CreateDbContext(string[] args)
    {
        var connectionString = ConnectionStrings.RequireMaster();

        var options = new DbContextOptionsBuilder<AthenaMasterDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(AthenaMasterDbContext).Assembly.FullName))
            .Options;

        return new AthenaMasterDbContext(options);
    }
}
