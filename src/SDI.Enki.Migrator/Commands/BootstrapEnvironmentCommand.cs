using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SDI.Enki.Identity.Bootstrap;
using SDI.Enki.Identity.Data;
using SDI.Enki.Infrastructure.Data;
using SDI.Enki.Shared.Configuration;

namespace SDI.Enki.Migrator.Commands;

/// <summary>
/// First-time environment bootstrap. Applies Identity + Master
/// migrations, runs the Tools / Calibrations master seed, then creates
/// the OpenIddict <c>enki-blazor</c> client + <c>enki</c> scope and the
/// initial admin user — all idempotent. After this command succeeds,
/// the app pools can be started against the freshly-staged DBs.
///
/// <para>
/// Tenant DBs are not touched. Use <c>provision</c> to create new
/// tenants once the WebApi is running, or <c>seed-demo-tenants</c>
/// for the dev showcase set.
/// </para>
///
/// <para>
/// Required configuration (no dev fallback in any environment):
/// </para>
/// <list type="bullet">
///   <item><c>ConnectionStrings:Master</c> — verified at Migrator startup.</item>
///   <item><c>ConnectionStrings:Identity</c></item>
///   <item><c>Identity:Seed:BlazorClientSecret</c> — must match the value the BlazorServer host's <c>Identity:ClientSecret</c> reads.</item>
///   <item><c>Identity:Seed:AdminEmail</c> — initial admin user's email.</item>
///   <item><c>Identity:Seed:AdminPassword</c> — initial admin user's password.</item>
/// </list>
///
/// <para>
/// Optional: <c>Identity:Seed:BlazorBaseUri</c> sets the OIDC
/// redirect targets. Defaults to the dev rig redirects when missing
/// (only useful in Development); any non-Dev environment must set this
/// to the BlazorServer host's public URL (e.g.
/// <c>https://dev.sdiamr.com/</c>).
/// </para>
/// </summary>
internal static class BootstrapEnvironmentCommand
{
    public static async Task<int> RunAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        try
        {
            RequiredSecretsValidator.Validate(
                configuration, environment,
                required:
                [
                    new("ConnectionStrings:Identity",
                        "Identity DB connection string."),
                    new("Identity:Seed:BlazorClientSecret",
                        "OIDC client secret for the Blazor client; must match the value on the BlazorServer host."),
                    new("Identity:Seed:AdminEmail",
                        "Email of the initial admin user (single Enki-admin Team-Office row)."),
                    new("Identity:Seed:AdminPassword",
                        "Password for the initial admin user. Change after first sign-in."),
                ]);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        // Resolve the redirect URI policy. Outside Development a public
        // BlazorBaseUri is mandatory — without it the OIDC client would
        // be created with localhost redirects and the staging/prod
        // Blazor host's signin-oidc callback would 400.
        Uri? blazorBaseUri = null;
        var rawBase = configuration["Identity:Seed:BlazorBaseUri"];
        if (!string.IsNullOrWhiteSpace(rawBase))
        {
            if (!Uri.TryCreate(rawBase, UriKind.Absolute, out blazorBaseUri))
            {
                Console.Error.WriteLine(
                    $"Identity:Seed:BlazorBaseUri = '{rawBase}' is not a valid absolute URI.");
                return 1;
            }
        }
        else if (!environment.IsDevelopment())
        {
            Console.Error.WriteLine(
                "Identity:Seed:BlazorBaseUri is required outside Development. " +
                "Set it to the BlazorServer host's public URL (e.g. https://dev.sdiamr.com/).");
            return 1;
        }

        await using var scope = services.CreateAsyncScope();
        var sp     = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Enki.Migrator.Bootstrap");

        try
        {
            // 1. Identity DB schema.
            logger.LogInformation("Bootstrap step 1/4 — Identity DB migrations...");
            var identityDb = sp.GetRequiredService<ApplicationDbContext>();
            await identityDb.Database.MigrateAsync();

            // 2. Master DB schema + canonical Tools / Calibrations.
            logger.LogInformation("Bootstrap step 2/4 — Master DB migrations + Tools/Calibrations seed...");
            var masterDb = sp.GetRequiredService<EnkiMasterDbContext>();
            await masterDb.Database.MigrateAsync();
            await MasterDataSeeder.SeedAsync(masterDb, logger);

            // 3. OpenIddict scope + Blazor client + admin user.
            logger.LogInformation("Bootstrap step 3/4 — OpenIddict client + admin user...");
            var bootstrapper = sp.GetRequiredService<IdentityBootstrapper>();
            var redirects = blazorBaseUri is null
                ? IdentityBootstrapper.DevRedirects()
                : IdentityBootstrapper.FromBlazorBaseUri(blazorBaseUri);

            await bootstrapper.BootstrapForProductionAsync(
                adminEmail:         configuration["Identity:Seed:AdminEmail"]!,
                adminPassword:      configuration["Identity:Seed:AdminPassword"]!,
                blazorClientSecret: configuration["Identity:Seed:BlazorClientSecret"]!,
                redirects:          redirects);

            // 4. Done.
            logger.LogInformation("Bootstrap step 4/4 — complete. Environment is ready for first sign-in.");
            Console.WriteLine();
            Console.WriteLine("Environment bootstrap complete.");
            Console.WriteLine($"  Admin email:        {configuration["Identity:Seed:AdminEmail"]}");
            Console.WriteLine($"  OIDC client:        {IdentityBootstrapper.BlazorClientId}");
            Console.WriteLine($"  Blazor redirect:    {string.Join(", ", redirects.RedirectUris)}");
            Console.WriteLine();
            Console.WriteLine("Next: start the IIS app pools and verify sign-in works before");
            Console.WriteLine("closing the deploy session — the admin password is single-tracked.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bootstrap FAILED.");
            Console.Error.WriteLine($"Bootstrap FAILED: {ex.Message}");
            return 2;
        }
    }
}
