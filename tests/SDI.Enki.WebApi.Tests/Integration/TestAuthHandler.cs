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
/// Each request can override the per-fixture defaults via headers:
/// <list type="bullet">
///   <item><see cref="SubHeader"/> — caller sub claim (overrides Options.UserId)</item>
///   <item><see cref="AdminHeader"/> — <c>"true"</c>/<c>"false"</c> to flip the admin role</item>
///   <item><see cref="AnonymousHeader"/> — present means "treat as unauthenticated"</item>
/// </list>
/// Tests that don't set any of these get the Options defaults — preserves
/// the simpler "everyone is admin" behaviour the smoke tests rely on.
/// </para>
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<TestAuthHandlerOptions>
{
    public const string SchemeName       = "Test";
    public const string SubHeader        = "X-Test-Sub";
    public const string AdminHeader      = "X-Test-Admin";
    public const string AnonymousHeader  = "X-Test-Anonymous";

    public TestAuthHandler(
        IOptionsMonitor<TestAuthHandlerOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Explicit "I am anonymous" — emit no principal so the pipeline
        // reaches authorization unauthenticated and returns 401.
        if (Request.Headers.ContainsKey(AnonymousHeader))
            return Task.FromResult(AuthenticateResult.NoResult());

        var sub = Request.Headers.TryGetValue(SubHeader, out var subHeader)
                  && !string.IsNullOrEmpty(subHeader)
            ? subHeader.ToString()
            : Options.UserId;

        var isAdmin = Request.Headers.TryGetValue(AdminHeader, out var adminHeader)
                  && !string.IsNullOrEmpty(adminHeader)
            ? bool.Parse(adminHeader!)
            : Options.IsAdmin;

        var claims = new List<Claim>
        {
            new("sub",    sub),
            new("name",   Options.UserName),
            // Scope claim layout matches what OpenIddict.Validation produces —
            // private "oi_scp" claim. EnkiApiScope policy reads via
            // RequireClaim(Claims.Private.Scope, "enki"), and Claims.Private.Scope
            // resolves to "oi_scp".
            new("oi_scp", "enki"),
        };
        if (isAdmin)
            claims.Add(new Claim("role", "enki-admin"));

        var identity  = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Per-scheme defaults for <see cref="TestAuthHandler"/>. Per-request
/// header overrides take precedence — see the handler comment.
/// </summary>
public sealed class TestAuthHandlerOptions : AuthenticationSchemeOptions
{
    public string UserId   { get; set; } = "11111111-1111-1111-1111-111111111111";
    public string UserName { get; set; } = "test.user";
    public bool   IsAdmin  { get; set; } = true;
}
