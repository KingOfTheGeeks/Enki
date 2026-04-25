using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SDI.Enki.WebApi.Tests.Integration;

/// <summary>
/// Authentication handler used by integration tests in place of the
/// real OpenIddict validation. Synthesises a principal with the claims
/// the policies expect — <c>sub</c>, the <c>enki</c> scope, and
/// optionally the <c>enki-admin</c> role — so a <c>TestServer</c> can
/// reach controllers behind <c>EnkiApiScope</c> / <c>CanAccessTenant</c>
/// without spinning up Identity.
///
/// <para>
/// The handler wires under a single scheme name (<see cref="SchemeName"/>);
/// the factory swaps the WebApi's <c>DefaultAuthenticateScheme</c> +
/// <c>DefaultChallengeScheme</c> to it during <c>ConfigureWebHost</c>.
/// </para>
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<TestAuthHandlerOptions>
{
    public const string SchemeName = "Test";

    public TestAuthHandler(
        IOptionsMonitor<TestAuthHandlerOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var opts = Options;

        var claims = new List<Claim>
        {
            new("sub",   opts.UserId),
            new("name",  opts.UserName),
            // Scope claim layout matches what OpenIddict.Validation produces —
            // private "oi_scp" claim. EnkiApiScope policy reads via
            // RequireClaim(Claims.Private.Scope, "enki"), and Claims.Private.Scope
            // resolves to "oi_scp".
            new("oi_scp", "enki"),
        };
        if (opts.IsAdmin)
            claims.Add(new Claim("role", "enki-admin"));

        var identity  = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Per-scheme options for <see cref="TestAuthHandler"/>. The test
/// factory mutates these per fixture so different tests can present
/// admin vs. regular-tenant-user principals against the same handler.
/// </summary>
public sealed class TestAuthHandlerOptions : AuthenticationSchemeOptions
{
    public string UserId   { get; set; } = "11111111-1111-1111-1111-111111111111";
    public string UserName { get; set; } = "test.user";
    public bool   IsAdmin  { get; set; } = true;
}
