using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SDI.Enki.Identity.Bootstrap;

namespace SDI.Enki.Identity.Data;

/// <summary>
/// Dev-rig seed shim. Resolves the Identity-seed credentials from
/// configuration (with the dev fallback when running under
/// <c>ASPNETCORE_ENVIRONMENT=Development</c>), then delegates to
/// <see cref="IdentityBootstrapper.SeedDevRosterAsync"/> for the
/// actual roster + OpenIddict provisioning. Kept as a separate class
/// so the dev-fallback behaviour stays plainly visible — the
/// bootstrapper itself takes credentials as parameters and never
/// touches configuration.
///
/// <para>
/// <b>Credential safety:</b> the default user password and Blazor
/// client secret come from configuration
/// (<c>Identity:Seed:DefaultUserPassword</c> and
/// <c>Identity:Seed:BlazorClientSecret</c>). When the host runs under
/// <c>ASPNETCORE_ENVIRONMENT=Development</c> a fallback to known dev
/// values is allowed; in any other environment a missing config value
/// throws — caller fails to start rather than silently seeding
/// well-known credentials into a real database.
/// </para>
/// </summary>
public static class IdentitySeedData
{
    /// <summary>OIDC client id; pinned to the bootstrapper's value.</summary>
    public const string BlazorClientId = IdentityBootstrapper.BlazorClientId;

    private const string DevFallbackUserPassword       = "Enki!dev1";
    private const string DevFallbackBlazorClientSecret = "enki-blazor-dev-secret";

    /// <summary>
    /// Apply the dev-rig seed: full <see cref="Shared.Seeding.SeedUsers"/>
    /// roster + OpenIddict scope + Blazor client. Idempotent.
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope   = services.CreateScope();
        var sp            = scope.ServiceProvider;
        var configuration = sp.GetRequiredService<IConfiguration>();
        var environment   = sp.GetRequiredService<IHostEnvironment>();
        var bootstrapper  = sp.GetRequiredService<IdentityBootstrapper>();

        var defaultPassword = ResolveCredential(
            configuration, environment,
            "Identity:Seed:DefaultUserPassword", DevFallbackUserPassword,
            humanName: "default user password");
        var clientSecret = ResolveCredential(
            configuration, environment,
            "Identity:Seed:BlazorClientSecret", DevFallbackBlazorClientSecret,
            humanName: "Blazor OIDC client secret");

        await bootstrapper.SeedDevRosterAsync(
            defaultPassword,
            clientSecret,
            IdentityBootstrapper.DevRedirects());
    }

    /// <summary>
    /// Pulls a credential from configuration. In Development, falls
    /// back to a known dev value if the config key is unset. In any
    /// other environment, throws — callers fail rather than silently
    /// using a well-known credential against a real database.
    /// </summary>
    internal static string ResolveCredential(
        IConfiguration   config,
        IHostEnvironment env,
        string           configKey,
        string           devFallback,
        string           humanName)
    {
        var value = config[configKey];
        if (!string.IsNullOrEmpty(value)) return value;

        if (env.IsDevelopment()) return devFallback;

        throw new InvalidOperationException(
            $"Missing required configuration value '{configKey}' " +
            $"for the {humanName}. Set it via appsettings, environment variable, " +
            $"or your secret store. Refusing to fall back to the dev default in " +
            $"environment '{env.EnvironmentName}'.");
    }
}
