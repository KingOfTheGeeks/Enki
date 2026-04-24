using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;

namespace SDI.Enki.BlazorServer.Auth;

/// <summary>
/// DelegatingHandler that lifts the current signed-in user's access token
/// from the auth ticket and attaches it as a <c>Bearer</c> header on
/// outgoing WebApi calls. Registered against the <c>"EnkiApi"</c> named
/// HttpClient in Program.cs.
///
/// Relies on <c>SaveTokens=true</c> in the OIDC config so the access_token
/// lives in the cookie-backed ticket rather than needing a round trip to
/// the token endpoint per call.
/// </summary>
public sealed class BearerTokenHandler(IHttpContextAccessor ctxAccessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var http = ctxAccessor.HttpContext;
        if (http is not null)
        {
            var token = await http.GetTokenAsync("access_token");
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
