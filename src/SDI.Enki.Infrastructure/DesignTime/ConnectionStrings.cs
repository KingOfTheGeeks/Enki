namespace SDI.Enki.Infrastructure.DesignTime;

/// <summary>
/// Helpers for design-time tooling (<c>dotnet ef</c>) that resolve
/// connection strings from environment variables. No fallbacks — a
/// missing variable throws with the exact env-var name + a copy-paste
/// PowerShell snippet, so the diagnostic is actionable.
///
/// <para>
/// Hardcoded credentials in source were a security regression: any
/// inspection of the compiled assemblies (or the repo, which is
/// private today but won't always be) leaked the dev <c>sa</c>
/// password. Forcing env-var resolution makes the credential
/// per-developer and per-machine.
/// </para>
/// </summary>
internal static class ConnectionStrings
{
    public const string MasterEnvVar = "EnkiMasterCs";

    public static string RequireMaster() =>
        Environment.GetEnvironmentVariable(MasterEnvVar)
        ?? throw new InvalidOperationException(
            $"Set the {MasterEnvVar} environment variable before invoking `dotnet ef` " +
            $"against the master or tenant DbContext. PowerShell example:\n\n" +
            $"  $env:{MasterEnvVar} = 'Server=10.1.7.50;Database=Enki_Master;" +
            $"User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;Encrypt=True;'\n");
}
