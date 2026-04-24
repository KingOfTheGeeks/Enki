using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace SDI.Enki.BlazorServer.Auth;

/// <summary>
/// DelegatingHandler that lifts the current signed-in user's access token
/// from the auth ticket and attaches it as a <c>Bearer</c> header on
/// outgoing WebApi calls. Registered against the <c>"EnkiApi"</c> named
/// HttpClient in Program.cs.
///
/// Relies on <c>SaveTokens=true</c> in the OIDC config so the access_token
/// lives in the cookie-backed ticket rather than needing a round trip to
/// the token endpoint per call. Reads explicitly from the Cookie scheme so
/// we don't depend on which scheme happens to be set as default.
/// </summary>
public sealed class BearerTokenHandler(
    IHttpContextAccessor ctxAccessor,
    ILogger<BearerTokenHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var http = ctxAccessor.HttpContext;
        if (http is null)
        {
            logger.LogWarning(
                "BearerTokenHandler: HttpContext is null for {Method} {Uri} — token cannot be attached. " +
                "This usually means the call is happening outside a server-render request " +
                "(e.g. InteractiveServer rendermode over SignalR).",
                request.Method, request.RequestUri);
        }
        else
        {
            // Authenticate explicitly against the Cookie scheme. Using the
            // untyped GetTokenAsync relies on the default AuthenticateScheme
            // resolving to Cookie, which isn't always true in Blazor Server.
            var auth = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (!auth.Succeeded)
            {
                logger.LogWarning(
                    "BearerTokenHandler: Cookie authentication did not succeed for {Method} {Uri}. " +
                    "Failure: {Failure}",
                    request.Method, request.RequestUri, auth.Failure?.Message ?? "(no Failure set)");
            }
            else
            {
                var token = auth.Properties?.GetTokenValue("access_token");
                if (string.IsNullOrEmpty(token))
                {
                    logger.LogWarning(
                        "BearerTokenHandler: Cookie auth succeeded for {Method} {Uri} but no access_token was present " +
                        "in the ticket properties. SaveTokens=true may not have taken effect.",
                        request.Method, request.RequestUri);
                }
                else
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    // Happy path: Debug level so it doesn't log per-request under default config.
                    // Flip WebApi / Blazor log levels to see it if ever needed.
                    logger.LogDebug(
                        "BearerTokenHandler: Attached Bearer token to {Method} {Uri}.",
                        request.Method, request.RequestUri);
                }
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
