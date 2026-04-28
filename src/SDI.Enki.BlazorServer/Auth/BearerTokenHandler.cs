using System.Net;
using System.Net.Http.Headers;

namespace SDI.Enki.BlazorServer.Auth;

/// <summary>
/// DelegatingHandler that lifts the current signed-in user's access
/// token from <see cref="CircuitTokenCache"/> and attaches it as a
/// <c>Bearer</c> header on outgoing WebApi calls. Registered against
/// the <c>"EnkiApi"</c> + <c>"EnkiIdentity"</c> named HttpClients in
/// Program.cs.
///
/// <para>
/// Earlier versions of this handler called
/// <c>HttpContext.AuthenticateAsync(Cookie)</c> per outbound request
/// — works inside Blazor Server circuits because the framework
/// preserves the original HttpContext, but means a non-trivial
/// authenticate call on every API hop and a "no token attached"
/// fallback whenever HttpContext happens to be null. The handler
/// now defers to <see cref="CircuitTokenCache"/>, which reads the
/// ticket once per circuit and serves cached tokens after.
/// </para>
///
/// <para>
/// On <see cref="HttpStatusCode.Unauthorized"/>, the cache is
/// invalidated so the next outbound call re-reads the auth ticket.
/// Useful when the user signed out via another tab — the next
/// API call sees the fresh "no token" state and the WebApi 401
/// surfaces cleanly to the page (which can prompt a re-login).
/// </para>
/// </summary>
public sealed class BearerTokenHandler(
    CircuitTokenCache tokenCache,
    ILogger<BearerTokenHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenCache.GetAccessTokenAsync();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            logger.LogDebug(
                "BearerTokenHandler: attached Bearer token to {Method} {Uri}.",
                request.Method, request.RequestUri);
        }
        else
        {
            logger.LogWarning(
                "BearerTokenHandler: no access_token available for {Method} {Uri}; " +
                "request will go out unauthenticated and the WebApi will likely 401.",
                request.Method, request.RequestUri);
        }

        var response = await base.SendAsync(request, cancellationToken);

        // 401 on the way back means the cached token is no good —
        // invalidate so the next call re-reads from the ticket.
        // Doesn't retry the request itself; pages that branch on
        // ApiErrorKind.Unauthenticated can decide what to do (re-login
        // banner, automatic redirect, etc).
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenCache.Invalidate();
            logger.LogDebug(
                "BearerTokenHandler: WebApi returned 401 for {Method} {Uri}; invalidated cached token.",
                request.Method, request.RequestUri);
        }

        return response;
    }
}
