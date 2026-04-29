using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using SDI.Enki.Identity.Auditing;
using SDI.Enki.Identity.Data;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SDI.Enki.Identity.Controllers;

/// <summary>
/// OpenIddict authorization-code + refresh-token flow endpoints.
/// OpenIddict's <c>AspNetCore</c> integration normalizes requests and then
/// forwards to whatever is routed at these paths (enabled via
/// <c>EnableAuthorizationEndpointPassthrough</c> and siblings in Program.cs).
///
/// <para>Class-level <see cref="EnableRateLimitingAttribute"/> applies the
/// <c>ConnectEndpoints</c> policy (10 req/min/IP) to every action — covers
/// the per-IP brute-force / refresh-spam surface that per-account lockout
/// can't reach.</para>
/// </summary>
[EnableRateLimiting("ConnectEndpoints")]
public sealed class AuthorizationController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IAuthEventLogger authEvents) : Controller
{
    /// <summary>GET /connect/authorize — the OIDC authorization endpoint.</summary>
    [HttpGet("~/connect/authorize"), HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("No OpenIddict request on this call.");

        // Check the user's cookie-backed identity. If not logged in, bounce
        // to the login page and come back here.
        var auth = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!auth.Succeeded || auth.Principal?.Identity?.IsAuthenticated != true)
        {
            return Challenge(
                authenticationSchemes: IdentityConstants.ApplicationScheme,
                properties: new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + QueryString.Create(
                        Request.HasFormContentType ? Request.Form.ToList() : Request.Query.ToList()),
                });
        }

        var user = await userManager.GetUserAsync(auth.Principal)
            ?? throw new InvalidOperationException("Authenticated principal not found in user store.");

        var principal = await signInManager.CreateUserPrincipalAsync(user);

        principal.SetScopes(request.GetScopes());
        principal.SetResources("resource_server_enki");

        foreach (var claim in principal.Claims)
            claim.SetDestinations(GetDestinations(claim, principal));

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>POST /connect/token — exchanges auth codes + refresh tokens for access/id tokens.</summary>
    [HttpPost("~/connect/token"), IgnoreAntiforgeryToken, Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("No OpenIddict request on this call.");

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            return BadRequest(new OpenIddictResponse
            {
                Error = Errors.UnsupportedGrantType,
                ErrorDescription = "Only authorization_code and refresh_token grants are supported.",
            });

        // Reconstruct the principal from the stored code / refresh token.
        var auth = await HttpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var user = await userManager.GetUserAsync(auth.Principal!);
        if (user is null || !await signInManager.CanSignInAsync(user))
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "User is no longer allowed to sign in.",
                }));
        }

        var principal = await signInManager.CreateUserPrincipalAsync(user);
        principal.SetScopes(auth.Principal!.GetScopes());
        principal.SetResources("resource_server_enki");

        foreach (var claim in principal.Claims)
            claim.SetDestinations(GetDestinations(claim, principal));

        // Audit the issuance — captures both auth-code exchange and
        // refresh-token rotations. The grant type is the security-
        // relevant signal: a sudden flood of refresh exchanges from
        // one IP looks different than one auth-code per session.
        var grantType = request.IsAuthorizationCodeGrantType()
            ? "authorization_code"
            : "refresh_token";
        await authEvents.LogAsync(
            eventType:  "TokenIssued",
            username:   user.UserName ?? "",
            identityId: user.Id,
            detail:     JsonSerializer.Serialize(new { grantType }),
            ct:         HttpContext.RequestAborted);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>GET /connect/userinfo — returns claims for the caller's bearer token.</summary>
    [HttpGet("~/connect/userinfo"), HttpPost("~/connect/userinfo")]
    [Microsoft.AspNetCore.Authorization.Authorize(AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)]
    public async Task<IActionResult> UserInfo()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var claims = new Dictionary<string, object>
        {
            [Claims.Subject] = user.Id,
        };

        if (User.HasScope(Scopes.Email) && user.Email is not null)
        {
            claims[Claims.Email]         = user.Email;
            claims[Claims.EmailVerified] = user.EmailConfirmed;
        }

        if (User.HasScope(Scopes.Profile))
        {
            if (user.UserName is not null) claims[Claims.PreferredUsername] = user.UserName;
            // Name / given_name / family_name come from claims stored in AspNetUserClaims.
            foreach (var key in new[] { Claims.Name, Claims.GivenName, Claims.FamilyName })
            {
                var c = User.FindFirst(key)?.Value;
                if (c is not null) claims[key] = c;
            }
        }

        return Ok(claims);
    }

    /// <summary>GET/POST /connect/logout — end-session endpoint.</summary>
    [HttpGet("~/connect/logout"), HttpPost("~/connect/logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        // Capture before SignOutAsync clears the principal — same
        // reason as Logout.cshtml.cs. This is the OIDC end-session
        // endpoint, the path BlazorServer's sign-out actually hits;
        // the Razor Page Logout handles the rare local-admin case.
        var user = User.Identity?.IsAuthenticated == true
            ? await userManager.GetUserAsync(User)
            : null;

        await signInManager.SignOutAsync();

        await authEvents.LogAsync(
            eventType:  "SignOut",
            username:   user?.UserName ?? "(anonymous)",
            identityId: user?.Id,
            ct:         HttpContext.RequestAborted);

        return SignOut(
            authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            properties: new AuthenticationProperties { RedirectUri = "/" });
    }

    /// <summary>
    /// Decides which tokens a given claim ends up in (access token, identity
    /// token, or both). The Subject / Name claims always go in both; everything
    /// else is gated on whether the matching OIDC scope was requested.
    /// </summary>
    private static IEnumerable<string> GetDestinations(Claim claim, ClaimsPrincipal principal) =>
        claim.Type switch
        {
            Claims.Name or Claims.Subject =>
                new[] { Destinations.AccessToken, Destinations.IdentityToken },
            Claims.Email or Claims.EmailVerified =>
                principal.HasScope(Scopes.Email)
                    ? new[] { Destinations.AccessToken, Destinations.IdentityToken }
                    : new[] { Destinations.AccessToken },
            Claims.GivenName or Claims.FamilyName or Claims.PreferredUsername =>
                principal.HasScope(Scopes.Profile)
                    ? new[] { Destinations.AccessToken, Destinations.IdentityToken }
                    : new[] { Destinations.AccessToken },
            Claims.Role =>
                principal.HasScope(Scopes.Roles)
                    ? new[] { Destinations.AccessToken, Destinations.IdentityToken }
                    : new[] { Destinations.AccessToken },
            // Never leak the security stamp.
            "AspNet.Identity.SecurityStamp" => Array.Empty<string>(),
            _ => new[] { Destinations.AccessToken },
        };
}
