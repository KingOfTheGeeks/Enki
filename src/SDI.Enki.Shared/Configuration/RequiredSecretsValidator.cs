using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SDI.Enki.Shared.Configuration;

/// <summary>
/// One required secret. The <see cref="Description"/> is rendered into
/// the fail-loud error message so an operator who sees the startup
/// crash knows what the missing key is for.
///
/// <para>
/// <see cref="ProductionOnly"/> is for keys that are mandatory in
/// Production but acceptable to skip in Staging or other non-Production
/// non-Development environments. The OIDC signing certificate is the
/// canonical example — Production needs a real PFX, Staging may run
/// against a development cert without harm.
/// </para>
/// </summary>
public sealed record RequiredSecret(
    string Key,
    string Description,
    bool ProductionOnly = false);

/// <summary>
/// One key that must NOT be set in non-Development environments.
/// Catches leakage of dev fallbacks into production — most importantly
/// the dev seed-user password, which exists for the local rig and must
/// never source production user credentials.
/// </summary>
public sealed record ProhibitedKey(
    string Key,
    string Reason);

/// <summary>
/// Startup-time validation that every required secret is present and
/// no prohibited dev-fallback key is set, in any non-Development
/// environment.
///
/// <para>
/// Each host calls <see cref="Validate"/> once, at the top of
/// <c>Program.cs</c>, with its own list of required and prohibited
/// keys. A missing required secret or a present prohibited key
/// produces an <see cref="InvalidOperationException"/> with a
/// human-readable message naming every offender — the host fails to
/// start.
/// </para>
///
/// <para>
/// Development is exempt: the dev rig uses fallbacks (in seeders, in
/// committed <c>appsettings.Development.json</c>) that would otherwise
/// trip the validator on every <c>dotnet run</c>. The class deliberately
/// short-circuits in Development so a developer doesn't have to set
/// every env var to boot the rig.
/// </para>
///
/// <para>
/// See <c>docs/deploy.md § Secret staging</c> for the canonical list
/// of secrets per host and the operational pattern around them.
/// </para>
/// </summary>
public static class RequiredSecretsValidator
{
    /// <summary>
    /// Run validation against the supplied configuration. Throws on
    /// any violation; returns silently on success.
    /// </summary>
    public static void Validate(
        IConfiguration configuration,
        IHostEnvironment environment,
        IEnumerable<RequiredSecret> required,
        IEnumerable<ProhibitedKey>? prohibited = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(required);

        // Development gets a free pass. The dev seeder + committed
        // appsettings.Development.json provide all the values the rig
        // needs; running every developer through a "set 8 env vars"
        // gate would be hostile.
        if (environment.IsDevelopment()) return;

        var missing       = new List<RequiredSecret>();
        var presentForbid = new List<ProhibitedKey>();

        foreach (var r in required)
        {
            // ProductionOnly secrets are skipped outside Production —
            // Staging may run against a dev cert, for example.
            if (r.ProductionOnly && !environment.IsProduction()) continue;

            if (string.IsNullOrWhiteSpace(configuration[r.Key]))
                missing.Add(r);
        }

        if (prohibited is not null)
        {
            foreach (var p in prohibited)
            {
                if (!string.IsNullOrWhiteSpace(configuration[p.Key]))
                    presentForbid.Add(p);
            }
        }

        if (missing.Count == 0 && presentForbid.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine($"Configuration validation failed for environment '{environment.EnvironmentName}'.");

        if (missing.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Missing required secrets:");
            foreach (var m in missing)
                sb.AppendLine($"  - {m.Key} : {m.Description}");
        }

        if (presentForbid.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Prohibited keys present (must NOT be set outside Development):");
            foreach (var p in presentForbid)
                sb.AppendLine($"  - {p.Key} : {p.Reason}");
        }

        sb.AppendLine();
        sb.AppendLine("See docs/deploy.md § Secret staging for the canonical list.");

        throw new InvalidOperationException(sb.ToString());
    }
}
