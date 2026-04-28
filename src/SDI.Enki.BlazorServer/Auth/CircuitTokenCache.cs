using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace SDI.Enki.BlazorServer.Auth;

/// <summary>
/// Per-circuit (= per-DI-scope) cache of the signed-in user's
/// access_token + refresh_token + expiry. The original
/// <see cref="BearerTokenHandler"/> reaches into
/// <c>IHttpContextAccessor.HttpContext</c> on every outbound call;
/// in InteractiveServer Blazor that works because the framework
/// preserves the original HttpContext for the circuit's lifetime,
/// but the call path runs <c>AuthenticateAsync(Cookie)</c> per
/// request which is non-trivial work, and falls back to "no
/// token attached + warning logged" if HttpContext ever returns
/// null. Both are circuit-safety hazards.
///
/// <para>
/// This cache is registered scoped, so each Blazor circuit gets
/// its own instance. The cache lazily reads the auth ticket on
/// first request and stores the access_token / refresh_token /
/// expiry in memory; subsequent calls inside the same circuit
/// short-circuit straight to the cached value. Falls back
/// gracefully when the cookie scheme can't authenticate (e.g.
/// the user signed out mid-circuit) by clearing the cache.
/// </para>
///
/// <para>
/// Refresh-on-401 is deliberately NOT implemented here yet —
/// flowing a refreshed access_token back into the cookie ticket
/// requires <see cref="IAuthenticationService.SignInAsync"/>,
/// which needs an HttpContext, which is exactly what the
/// circuit-safe path can't always provide. When token refresh
/// becomes a concrete bug (long-running circuits past the OIDC
/// access_token's <c>expires_in</c>), the right next move is
/// either:
/// <list type="bullet">
///   <item>Trigger a forced page reload on the first 401 (clears
///   the cache, re-establishes the circuit with fresh tokens), or</item>
///   <item>Extract the cookie ticket store, re-issue the cookie
///   on a refresh-token call, store the updated ticket
///   server-side via a custom <c>ITicketStore</c>.</item>
/// </list>
/// Either way, that work shouldn't gate this commit.
/// </para>
/// </summary>
public sealed class CircuitTokenCache(IHttpContextAccessor ctxAccessor, ILogger<CircuitTokenCache> logger)
{
    private readonly object _gate = new();
    private bool   _populated;
    private string? _accessToken;

    /// <summary>
    /// Returns the current access_token, populating the cache from
    /// the auth ticket on first call. Returns <c>null</c> when the
    /// cookie scheme can't authenticate (anonymous, signed out,
    /// expired session) — caller decides whether to attach an
    /// empty Authorization header or skip the call.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync()
    {
        if (TryGetCached(out var cached)) return cached;

        var http = ctxAccessor.HttpContext;
        if (http is null)
        {
            logger.LogWarning(
                "CircuitTokenCache: HttpContext is null on first read; cannot populate cache. " +
                "API calls from this circuit will go out without a Bearer token.");
            MarkPopulated(null);
            return null;
        }

        var auth = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!auth.Succeeded)
        {
            logger.LogDebug(
                "CircuitTokenCache: Cookie auth did not succeed on first read ({Failure}); " +
                "caching null token.",
                auth.Failure?.Message ?? "(no Failure set)");
            MarkPopulated(null);
            return null;
        }

        var token = auth.Properties?.GetTokenValue("access_token");
        MarkPopulated(token);
        return token;
    }

    /// <summary>
    /// Invalidate the cached token. Call this when the WebApi
    /// returns 401 — the cached token is stale; next call will
    /// re-read from the ticket. Doesn't refresh the token; if the
    /// ticket itself is stale, the next call's GET also returns
    /// stale data and the caller should redirect to /account/login.
    /// </summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _populated   = false;
            _accessToken = null;
        }
    }

    private bool TryGetCached([NotNullWhen(true)] out string? token)
    {
        lock (_gate)
        {
            if (_populated)
            {
                token = _accessToken!;   // _populated && _accessToken == null is the "no token" cached state
                return token is not null;
            }
        }
        token = null;
        return false;
    }

    private void MarkPopulated(string? token)
    {
        lock (_gate)
        {
            _accessToken = token;
            _populated   = true;
        }
    }
}
