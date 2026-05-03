using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SDI.Enki.Shared.Configuration;

namespace SDI.Enki.Core.Tests.Configuration;

/// <summary>
/// Unit cover for the secret-validator that runs at startup in every
/// production host. Failure modes here translate directly to "the host
/// silently boots without a critical secret" or "the host crashes when
/// it shouldn't" — both are bad, both should regress loudly.
/// </summary>
public sealed class RequiredSecretsValidatorTests
{
    [Fact]
    public void Development_environment_short_circuits_and_skips_validation()
    {
        // Even with a missing required key, Development should pass —
        // the dev rig relies on seeders + fallbacks that would
        // otherwise trip the validator on every dotnet run.
        var config = BuildConfig(); // no values
        var env    = new TestHostEnvironment("Development");

        var act = () => RequiredSecretsValidator.Validate(
            config,
            env,
            required: [new("Required:Key", "Some required value.")]);

        act();   // does not throw
    }

    [Fact]
    public void Production_throws_when_a_required_secret_is_missing()
    {
        var config = BuildConfig();   // no values
        var env    = new TestHostEnvironment("Production");

        var act = () => RequiredSecretsValidator.Validate(
            config,
            env,
            required: [new("ConnectionStrings:Master", "Master DB connection.")]);

        var ex = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("ConnectionStrings:Master", ex.Message);
        Assert.Contains("Master DB connection.", ex.Message);
    }

    [Fact]
    public void Production_passes_when_every_required_secret_is_present()
    {
        var config = BuildConfig(("ConnectionStrings:Master", "Server=...;"));
        var env    = new TestHostEnvironment("Production");

        var act = () => RequiredSecretsValidator.Validate(
            config,
            env,
            required: [new("ConnectionStrings:Master", "Master DB connection.")]);

        act();   // does not throw
    }

    [Fact]
    public void Production_only_secret_is_skipped_in_Staging()
    {
        // Production-only secrets (e.g. the OIDC signing PFX) are
        // mandatory in Production but acceptable to omit in Staging,
        // where a development cert may be in use.
        var config = BuildConfig();   // no values
        var env    = new TestHostEnvironment("Staging");

        var act = () => RequiredSecretsValidator.Validate(
            config,
            env,
            required:
            [
                new("Identity:SigningCertificate:Path",
                    "Path to the OIDC signing PFX.",
                    ProductionOnly: true),
            ]);

        act();   // does not throw — Production-only doesn't apply in Staging
    }

    [Fact]
    public void Production_only_secret_is_enforced_in_Production()
    {
        var config = BuildConfig();
        var env    = new TestHostEnvironment("Production");

        var act = () => RequiredSecretsValidator.Validate(
            config,
            env,
            required:
            [
                new("Identity:SigningCertificate:Path",
                    "Path to the OIDC signing PFX.",
                    ProductionOnly: true),
            ]);

        var ex = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("Identity:SigningCertificate:Path", ex.Message);
    }

    [Fact]
    public void Production_throws_when_a_prohibited_dev_fallback_is_set()
    {
        // The dev seed-user password must never source production
        // credentials; the validator catches a leak from a misconfigured
        // env file.
        var config = BuildConfig(
            ("ConnectionStrings:Master", "Server=...;"),
            ("Identity:Seed:DefaultUserPassword", "Enki!dev1"));
        var env    = new TestHostEnvironment("Production");

        var act = () => RequiredSecretsValidator.Validate(
            config,
            env,
            required:
            [
                new("ConnectionStrings:Master", "Master DB."),
            ],
            prohibited:
            [
                new("Identity:Seed:DefaultUserPassword",
                    "Dev seed default; never set in production."),
            ]);

        var ex = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("Identity:Seed:DefaultUserPassword", ex.Message);
        Assert.Contains("Prohibited keys present", ex.Message);
    }

    [Fact]
    public void Empty_or_whitespace_value_counts_as_missing()
    {
        // Common operational mistake: env-var set to "" (e.g. through
        // a misformatted secrets.env line). The validator must treat
        // whitespace-only values as missing.
        var config = BuildConfig(("ConnectionStrings:Master", "   "));
        var env    = new TestHostEnvironment("Production");

        var act = () => RequiredSecretsValidator.Validate(
            config,
            env,
            required: [new("ConnectionStrings:Master", "Master DB.")]);

        var ex = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("ConnectionStrings:Master", ex.Message);
    }

    [Fact]
    public void Single_error_message_lists_every_offender()
    {
        // When multiple secrets are missing, the operator sees them
        // all in one message — saves a deploy-fix-deploy-fix cycle.
        var config = BuildConfig();
        var env    = new TestHostEnvironment("Production");

        var act = () => RequiredSecretsValidator.Validate(
            config,
            env,
            required:
            [
                new("ConnectionStrings:Master", "Master DB."),
                new("Identity:Issuer", "OIDC issuer URL."),
            ]);

        var ex = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("ConnectionStrings:Master", ex.Message);
        Assert.Contains("Identity:Issuer", ex.Message);
    }

    [Fact]
    public void Error_message_points_at_the_canonical_doc()
    {
        var config = BuildConfig();
        var env    = new TestHostEnvironment("Production");

        var act = () => RequiredSecretsValidator.Validate(
            config,
            env,
            required: [new("Some:Key", "Description.")]);

        var ex = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("docs/deploy.md", ex.Message);
    }

    // ---- harness ----

    private static IConfiguration BuildConfig(params (string Key, string Value)[] values)
    {
        var dict = values.ToDictionary(v => v.Key, v => (string?)v.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict!).Build();
    }

    private sealed class TestHostEnvironment(string envName) : IHostEnvironment
    {
        public string EnvironmentName        { get; set; } = envName;
        public string ApplicationName        { get; set; } = "TestApp";
        public string ContentRootPath        { get; set; } = "/test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
