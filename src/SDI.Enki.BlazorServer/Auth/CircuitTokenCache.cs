using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace SDI.Enki.BlazorServer.Auth;

/// <summary>
/// Per-circuit (= per-DI-scope) cache of the signed-in user's
/// access_token, with proactive refresh against the OIDC token
/// endpoint when the cached token is close to expiring. The original
/// <see cref="BearerTokenHandler"/> reaches into
/// <c>IHttpContextAccessor.HttpContext</c> on every outbound call;
/// in InteractiveServer Blazor that works because the framework
/// preserves the original HttpContext for the circuit's lifetime,
/// but the call path runs <c>AuthenticateAsync(Cookie)</c> per
/// request which is non-trivial work, and falls back to "no
/// token attached + warning logged" if HttpContext ever returns
/// null.
///
/// <para>
/// This cache is registered scoped, so each Blazor circuit gets
/// its own instance. The cache lazily reads the auth ticket on
/// first request and stores the access_token + refresh_token +
/// expires_at; subsequent calls inside the same circuit short-
/// circuit straight to the cached value. When the cached
/// access_token is within <see cref="ExpiryGuard"/> of expiry, it
/// swaps the cached refresh_token for a fresh pair via
/// <c>POST {authority}/connect/token</c> (grant_type=refresh_token)
/// before returning the new access_token. The cookie's stored
/// access_token is NOT updated on refresh — that would require a
/// fresh <c>SignInAsync</c> against the response, which is fragile
/// mid-circuit. Instead the next circuit (post-page-reload) goes
/// through the same dance, using the cookie's still-valid
/// refresh_token.
/// </para>
///
/// <para>
/// This works because the Identity server has rolling refresh
/// tokens disabled (<c>DisableRollingRefreshTokens()</c> in
/// <c>SDI.Enki.Identity/Program.cs</c>) — the cookie's refresh_token
/// is reusable until the refresh_token's own lifetime expires
/// (default 10 days, see <c>SessionLifetimeOptions</c>). If rolling
/// is ever re-enabled, the cookie's refresh_token becomes single-use:
/// the first cold-start refresh succeeds, every subsequent one
/// (page reload, sign-out elsewhere, 401-driven <see cref="Invalidate"/>)
/// re-reads the now-redeemed token and fails with <c>invalid_grant</c>,
/// so the cache cycles 401 → Invalidate → 401 forever. Escalate to a
/// custom <c>ITicketStore</c> if rolling needs to come back on.
/// </para>
///
/// <para>
/// On 401 from the WebApi, <see cref="BearerTokenHandler"/> calls
/// <see cref="Invalidate"/> so the next outbound request goes
/// through the read+refresh path again. Useful when the user
/// signed out via another tab — the next API call sees a fresh
/// "no token" state and the WebApi 401 surfaces cleanly to the
/// page (which can prompt a re-login).
/// </para>
///
/// <para>
/// Additionally, every <see cref="GetAccessTokenAsync"/> peeks the
/// current cookie principal's <c>sub</c> claim and invalidates when
/// it doesn't match the value captured at last populate. Blazor
/// retains a circuit (and its scoped DI graph) across reconnect
/// windows; without this guard a sign-out + sign-in in the same tab
/// would inherit the previous user's bearer because the cache
/// instance and its <c>_accessToken</c> field outlive the cookie
/// change. The 401-on-stale-token recovery doesn't catch this case
/// because the previous bearer is still valid — the WebApi returns
/// 403 against the new user's privileges, not 401.
/// </para>
/// </summary>
public sealed class CircuitTokenCache(
    IHttpContextAccessor ctxAccessor,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<CircuitTokenCache> logger)
{
    /// <summary>
    /// Refresh window. If the cached access_token expires within this
    /// many seconds of "now", the cache will refresh proactively
    /// rather than risk a mid-call 401. 30 s is enough headroom for
    /// a long survey-import upload on a slow network without being
    /// so wide that it forces a refresh on every page load.
    /// </summary>
    private static readonly TimeSpan ExpiryGuard = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private bool    _populated;
    private string? _accessToken;
    private string? _cachedSub;

    /// <summary>
    /// Returns the current access_token, populating the cache from
    /// the auth ticket on first call and refreshing it if it's close
    /// to expiry. Returns <c>null</c> when the cookie scheme can't
    /// authenticate (anonymous, signed out, expired session) or when
    /// the refresh attempt failed (revoked refresh_token, Identity
    /// down) — caller decides whether to attach an empty
    /// Authorization header or skip the call.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        var http = ctxAccessor.HttpContext;

        // Bug G defence: if the cookie principal's `sub` no longer
        // matches the cached value, the underlying user has changed
        // (sign-out + sign-in into the same surviving circuit). Treat
        // the cache as cold so the next read re-binds against the new
        // ticket. HttpContext can be null mid-circuit (SignalR-only
        // operations) — in that case we can't validate, so fall through
        // to the cached value and rely on the WebApi 401 → Invalidate
        // path for any stale-token recovery.
        if (http is not null)
        {
            var currentSub = http.User.FindFirst("sub")?.Value;
            if (CachedUserChanged(currentSub))
            {
                logger.LogDebug(
                    "CircuitTokenCache: principal changed within the circuit " +
                    "({CachedSub} → {CurrentSub}); invalidating cached token.",
                    _cachedSub ?? "(none)", currentSub ?? "(anonymous)");
                Invalidate();
            }
        }

        if (TryGetCached(out var cached)) return cached;

        if (http is null)
        {
            logger.LogWarning(
                "CircuitTokenCache: HttpContext is null on first read; cannot populate cache. " +
                "API calls from this circuit will go out without a Bearer token.");
            MarkPopulated(null, sub: null);
            return null;
        }

        var auth = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!auth.Succeeded)
        {
            logger.LogDebug(
                "CircuitTokenCache: Cookie auth did not succeed on first read ({Failure}); " +
                "caching null token.",
                auth.Failure?.Message ?? "(no Failure set)");
            MarkPopulated(null, sub: null);
            return null;
        }

        var ticketSub    = auth.Principal?.FindFirst("sub")?.Value;
        var accessToken  = auth.Properties?.GetTokenValue("access_token");
        var refreshToken = auth.Properties?.GetTokenValue("refresh_token");
        var expiresAtRaw = auth.Properties?.GetTokenValue("expires_at");

        // Decide whether the cookie's access_token is fresh enough to
        // hand back as-is. Three branches:
        //   1. No access_token → null cache (nothing we can do).
        //   2. Has access_token + expires_at says fresh → cache + return.
        //   3. Has access_token + expires_at says stale (or missing) +
        //      has refresh_token → attempt refresh.
        if (string.IsNullOrEmpty(accessToken))
        {
            MarkPopulated(null, ticketSub);
            return null;
        }

        if (TryParseExpiresAt(expiresAtRaw, out var expiresAt)
            && expiresAt - DateTimeOffset.UtcNow > ExpiryGuard)
        {
            MarkPopulated(accessToken, ticketSub);
            return accessToken;
        }

        // Fall-through = stale or unparseable expires_at. Attempt
        // refresh if we have a refresh_token; otherwise hand the
        // (possibly-stale) access_token back and let the WebApi 401
        // drive the user to re-auth.
        if (!string.IsNullOrEmpty(refreshToken))
        {
            var refreshed = await TryRefreshAsync(refreshToken, ct);
            if (refreshed is not null)
            {
                MarkPopulated(refreshed, ticketSub);
                return refreshed;
            }
            logger.LogInformation(
                "CircuitTokenCache: refresh_token grant failed; falling back to the " +
                "(probably-stale) access_token from the cookie. The next API call will " +
                "likely 401 and the user will need to sign in again.");
        }

        MarkPopulated(accessToken, ticketSub);
        return accessToken;
    }

    /// <summary>
    /// Invalidate the cached token. Call this when the WebApi
    /// returns 401 — the cached token is stale; next call will
    /// re-read from the ticket and (if eligible) refresh.
    /// </summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _populated   = false;
            _accessToken = null;
            _cachedSub   = null;
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

    private void MarkPopulated(string? token, string? sub)
    {
        lock (_gate)
        {
            _accessToken = token;
            _cachedSub   = sub;
            _populated   = true;
        }
    }

    /// <summary>
    /// True when the principal driving the circuit's HttpContext now
    /// resolves to a different <c>sub</c> than the one captured at
    /// last cache population. Anonymous (no sub) on the current
    /// principal counts as a change — the previous user signed out
    /// and the next outbound call shouldn't reuse their bearer.
    /// </summary>
    private bool CachedUserChanged(string? currentSub)
    {
        lock (_gate)
        {
            if (!_populated) return false;
            return !string.Equals(currentSub, _cachedSub, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// expires_at on an OIDC auth ticket is usually a round-trip
    /// ISO-8601 timestamp; older handlers wrote a Unix-epoch seconds
    /// integer. Parse both shapes so the cache works against either.
    /// </summary>
    private static bool TryParseExpiresAt(string? raw, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        if (string.IsNullOrEmpty(raw)) return false;

        if (DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var iso))
        {
            expiresAt = iso;
            return true;
        }
        if (long.TryParse(raw, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var unix))
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(unix);
            return true;
        }
        return false;
    }

    /// <summary>
    /// POST <c>grant_type=refresh_token</c> against the OIDC token
    /// endpoint. Uses the "EnkiIdentityNoAuth" named HttpClient (no
    /// <see cref="BearerTokenHandler"/>) so the call doesn't try to
    /// attach our own Bearer header — the form body's
    /// client_id / client_secret is the auth.
    /// Returns the fresh access_token on success; <c>null</c> on any
    /// failure (refresh_token rejected / Identity unreachable / wire
    /// shape unexpected). Logs on failure so a real outage is
    /// visible in the host logs.
    /// </summary>
    private async Task<string?> TryRefreshAsync(string refreshToken, CancellationToken ct)
    {
        try
        {
            var clientId     = configuration["Identity:ClientId"]     ?? "enki-blazor";
            var clientSecret = configuration["Identity:ClientSecret"] ?? "";

            var client = httpClientFactory.CreateClient("EnkiIdentityNoAuth");
            using var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("grant_type",    "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", refreshToken),
                new KeyValuePair<string, string>("client_id",     clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
            ]);
            using var resp = await client.PostAsync("connect/token", content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "CircuitTokenCache: refresh_token grant returned {Status}. The cookie's " +
                    "refresh_token may be expired or revoked; user will need to re-auth.",
                    (int)resp.StatusCode);
                return null;
            }

            var body = await resp.Content.ReadFromJsonAsync<TokenResponse>(ct);
            if (body is null || string.IsNullOrEmpty(body.AccessToken))
            {
                logger.LogWarning(
                    "CircuitTokenCache: refresh_token grant returned 200 but no access_token in body.");
                return null;
            }
            return body.AccessToken;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "CircuitTokenCache: refresh_token grant threw; treating as failed refresh.");
            return null;
        }
    }

    /// <summary>
    /// Wire shape of an OIDC token response. We only need the
    /// access_token; the rest are deserialised for completeness in
    /// case a future caller wants to honour <c>expires_in</c> or
    /// rotate the refresh_token in the cookie.
    /// </summary>
    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")]  string  AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")]    int?    ExpiresIn,
        [property: JsonPropertyName("token_type")]    string? TokenType);
}
