using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SDI.Enki.Identity.Data;

namespace SDI.Enki.Identity.DesignTime;

/// <summary>
/// Design-time factory for <see cref="ApplicationDbContext"/>. Enables
/// <c>dotnet ef migrations add</c> / <c>database update</c> without
/// booting the full host.
///
/// <para>
/// <b>No fallback connection string.</b> Set the <c>EnkiIdentityCs</c>
/// environment variable before invoking <c>dotnet ef</c> against this
/// project. PowerShell example:
/// <code>$env:EnkiIdentityCs = 'Server=10.1.7.50;Database=Enki_Identity;User Id=...;Password=...;TrustServerCertificate=True;'</code>
/// </para>
/// </summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    private const string IdentityEnvVar = "EnkiIdentityCs";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(IdentityEnvVar)
            ?? throw new InvalidOperationException(
                $"Set the {IdentityEnvVar} environment variable before invoking `dotnet ef` " +
                $"against the Identity DbContext. PowerShell example:\n\n" +
                $"  $env:{IdentityEnvVar} = 'Server=10.1.7.50;Database=Enki_Identity;" +
                $"User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;Encrypt=True;'\n");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseOpenIddict()
            .Options;

        return new ApplicationDbContext(options);
    }
}
