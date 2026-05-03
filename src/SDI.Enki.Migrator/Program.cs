using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SDI.Enki.Identity.Bootstrap;
using SDI.Enki.Identity.Data;
using SDI.Enki.Infrastructure;
using SDI.Enki.Migrator.Commands;
using Serilog;

// Enki Migrator — CLI for environment bootstrap, schema fan-out, and
// tenant provisioning.
//
//   Enki.Migrator help
//   Enki.Migrator provision               --code EXXON --name "ExxonMobil" [options]
//   Enki.Migrator migrate-identity
//   Enki.Migrator migrate-master
//   Enki.Migrator migrate-tenants         [--all | --tenants EXXON,CVX] [--parallel 4]
//   Enki.Migrator migrate                  (alias for migrate-tenants)
//   Enki.Migrator migrate-all
//   Enki.Migrator bootstrap-environment   (migrate-identity + migrate-master + seed admin + OIDC)
//   Enki.Migrator seed-demo-tenants       (Dev convenience: provisions PERMIAN / NORTHSEA / BOREAL)
//
// Configuration comes from appsettings.json + appsettings.Development.json +
// environment variables (standard Host.CreateApplicationBuilder precedence).
// ConnectionStrings:Master is required at startup (every command except
// migrate-identity touches the master DB). ConnectionStrings:Identity is
// required for the identity-touching commands; the DbContext registration
// is lazy so a Master-only command doesn't need the Identity connection
// string set.

// Help short-circuit — must work without any configuration.
if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    return HelpCommand.Print();

// Every other command needs the master connection string.
// ContentRootPath is explicitly set to AppContext.BaseDirectory (the bin
// folder) so appsettings.json + appsettings.Development.json are read from
// where MSBuild copied them, regardless of the shell's current directory.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

var masterConn = builder.Configuration.GetConnectionString("Master");
if (string.IsNullOrWhiteSpace(masterConn))
{
    Console.Error.WriteLine(
        "ConnectionStrings:Master is not configured. " +
        "Set it in appsettings.Development.json or via the " +
        "ConnectionStrings__Master environment variable.");
    return 1;
}

// Master DB stack: registers EnkiMasterDbContext, ProvisioningOptions,
// DatabaseAdmin, ITenantProvisioningService.
builder.Services.AddEnkiInfrastructure(masterConn);

// Identity DB stack: registered with the delegate overload so a missing
// ConnectionStrings:Identity is only fatal when a command actually
// requests ApplicationDbContext (i.e. Identity-touching commands). A
// pure migrate-master / migrate-tenants invocation doesn't need Identity
// configured at all.
builder.Services.AddDbContext<ApplicationDbContext>((sp, opt) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var identityConn = config.GetConnectionString("Identity")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:Identity is required for this command. " +
            "Set it via appsettings or the ConnectionStrings__Identity " +
            "environment variable.");
    opt.UseSqlServer(identityConn, sql =>
    {
        // Same retry posture as the Identity host — six attempts,
        // 10 s back-off — so a cold dev SQL Server doesn't flake the
        // first migration call.
        sql.EnableRetryOnFailure(
            maxRetryCount: 6,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null);
    });
    // Register OpenIddict's EF entity sets via the same DbContext —
    // required for the OpenIddict.EntityFrameworkCore stores to find
    // their tables on this connection.
    opt.UseOpenIddict();
});

// AspNetCore.Identity surface used by the bootstrapper. AddIdentityCore
// (not AddIdentity) keeps things minimal — no auth pipeline, just the
// UserManager + RoleManager stores.
//
// Note: AddDefaultTokenProviders() is intentionally NOT called.
// DataProtectorTokenProvider depends on IDataProtectionProvider, which
// only the web hosts auto-register; the CLI host doesn't, and the
// bootstrapper doesn't issue any tokens (no password reset, no email
// confirmation), so we skip the provider stack entirely. If a future
// command needs tokens, add `services.AddDataProtection()` alongside.
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        // Match the Identity host's password rules. A weak admin
        // password supplied to bootstrap-environment surfaces here as
        // a CreateAsync failure with a clear error.
        options.Password.RequireDigit           = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredLength         = 8;
        options.Password.RequireUppercase       = true;
        options.Password.RequireLowercase       = true;
        options.User.RequireUniqueEmail         = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// OpenIddict core — registers IOpenIddictApplicationManager +
// IOpenIddictScopeManager backed by the EF stores. No Server / Validation
// components: this is the deploy-time CLI, not the auth host.
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<ApplicationDbContext>();
    });

// The bootstrapper itself.
builder.Services.AddScoped<IdentityBootstrapper>();

// Serilog — same pattern as the web hosts. Console output keeps the
// interactive CLI readable; file output captures full structured detail
// for long provision / migrate runs so we can reconstruct what happened.
builder.Services.AddSerilog((sp, cfg) => cfg
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(sp)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Enki.Migrator")
    .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
    .WriteTo.Console(
        outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/enki-migrator-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14));

var app    = builder.Build();
var config = builder.Configuration;
var env    = builder.Environment;

return args[0] switch
{
    "provision"             => await ProvisionCommand.RunAsync(app.Services, args[1..]),
    "migrate-identity"      => await MigrateIdentityCommand.RunAsync(app.Services, config, env),
    "migrate-master"        => await MigrateMasterCommand.RunAsync(app.Services),
    "migrate-tenants"       => await MigrateCommand.RunAsync(app.Services, args[1..]),
    "migrate"               => await MigrateCommand.RunAsync(app.Services, args[1..]),
    "migrate-all"           => await MigrateAllCommand.RunAsync(app.Services, config, env, args[1..]),
    "bootstrap-environment" => await BootstrapEnvironmentCommand.RunAsync(app.Services, config, env),
    "dev-bootstrap"         => await DevBootstrapCommand.RunAsync(app.Services, env),
    "seed-demo-tenants"     => await SeedDemoTenantsCommand.RunAsync(app.Services),
    _                       => HelpCommand.Unknown(args[0]),
};
