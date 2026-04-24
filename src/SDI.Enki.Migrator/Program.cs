using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SDI.Enki.Infrastructure;
using SDI.Enki.Migrator.Commands;
using Serilog;

// Enki Migrator — CLI for tenant provisioning and schema fan-out.
//
//   Enki.Migrator provision --code EXXON --name "ExxonMobil" [options]
//   Enki.Migrator migrate   [--all | --tenants EXXON,CVX] [--parallel 4]
//
// Configuration comes from appsettings.json + appsettings.Development.json +
// environment variables (standard Host.CreateApplicationBuilder precedence).

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

builder.Services.AddEnkiInfrastructure(masterConn);

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

var app = builder.Build();

return args[0] switch
{
    "provision" => await ProvisionCommand.RunAsync(app.Services, args[1..]),
    "migrate"   => await MigrateCommand.RunAsync(app.Services, args[1..]),
    _           => HelpCommand.Unknown(args[0]),
};
