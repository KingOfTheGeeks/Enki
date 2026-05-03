using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SDI.Enki.Migrator.Commands;

namespace SDI.Enki.Migrator.Tests;

/// <summary>
/// Coverage for <see cref="BootstrapEnvironmentCommand"/>'s pre-DB
/// validation gates. These tests don't need a SQL Server container
/// because the command returns before any DbContext is requested when
/// validation fails — they cover the contract that a missing or
/// invalid env-var stops the command before it can corrupt anything.
///
/// <para>
/// The "happy path" (full bootstrap against an empty DB) is exercised
/// by the lower-layer
/// <see cref="IdentityBootstrapperTests.BootstrapForProductionAsync_OnEmptyDb_CreatesAdminAndOpenIddictClient"/>
/// — repeating the full E2E here would only duplicate that coverage.
/// </para>
/// </summary>
public class BootstrapEnvironmentCommandTests
{
    private sealed class StubEnv(string envName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = envName;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IServiceProvider EmptyServices() =>
        new ServiceCollection().AddLogging().BuildServiceProvider();

    [Fact]
    public async Task BootstrapEnvironment_MissingAdminPassword_FailsBeforeDbWork()
    {
        // Validator runs before any service is resolved; an empty
        // service provider is enough to prove the command returns 1
        // and never opens a DB connection.
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Master"]         = "Server=fake;Database=NotReal;",
            ["ConnectionStrings:Identity"]       = "Server=fake;Database=NotReal;",
            ["Identity:Seed:BlazorClientSecret"] = "secret",
            ["Identity:Seed:AdminEmail"]         = "admin@test.local",
            ["Identity:Seed:BlazorBaseUri"]      = "https://test.local/",
            // ... AdminPassword deliberately missing
        });

        var exit = await BootstrapEnvironmentCommand.RunAsync(
            EmptyServices(), config, new StubEnv("Staging"));

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task BootstrapEnvironment_MissingBlazorClientSecret_FailsBeforeDbWork()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Master"]    = "Server=fake;Database=NotReal;",
            ["ConnectionStrings:Identity"]  = "Server=fake;Database=NotReal;",
            ["Identity:Seed:AdminEmail"]    = "admin@test.local",
            ["Identity:Seed:AdminPassword"] = "Pwd!1234",
            ["Identity:Seed:BlazorBaseUri"] = "https://test.local/",
            // ... BlazorClientSecret deliberately missing
        });

        var exit = await BootstrapEnvironmentCommand.RunAsync(
            EmptyServices(), config, new StubEnv("Staging"));

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task BootstrapEnvironment_NonDevWithoutBlazorBaseUri_FailsBeforeDbWork()
    {
        // All the secret keys are present, but BlazorBaseUri is missing
        // — outside Development that's a fatal misconfiguration because
        // the OIDC client would otherwise be created with localhost
        // redirects against a non-localhost Blazor host.
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Master"]         = "Server=fake;Database=NotReal;",
            ["ConnectionStrings:Identity"]       = "Server=fake;Database=NotReal;",
            ["Identity:Seed:BlazorClientSecret"] = "secret",
            ["Identity:Seed:AdminEmail"]         = "admin@test.local",
            ["Identity:Seed:AdminPassword"]      = "Pwd!1234",
            // ... BlazorBaseUri deliberately missing
        });

        var exit = await BootstrapEnvironmentCommand.RunAsync(
            EmptyServices(), config, new StubEnv("Staging"));

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task BootstrapEnvironment_BadlyFormedBlazorBaseUri_FailsBeforeDbWork()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Master"]         = "Server=fake;Database=NotReal;",
            ["ConnectionStrings:Identity"]       = "Server=fake;Database=NotReal;",
            ["Identity:Seed:BlazorClientSecret"] = "secret",
            ["Identity:Seed:AdminEmail"]         = "admin@test.local",
            ["Identity:Seed:AdminPassword"]      = "Pwd!1234",
            ["Identity:Seed:BlazorBaseUri"]      = "not a uri at all",
        });

        var exit = await BootstrapEnvironmentCommand.RunAsync(
            EmptyServices(), config, new StubEnv("Staging"));

        Assert.Equal(1, exit);
    }
}
