using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SDI.Enki.BlazorServer.Auth;

namespace SDI.Enki.BlazorServer.Tests.Auth;

/// <summary>
/// Regression cover for Bug G — the cross-user bearer-token leak that
/// arose when Blazor retained a SignalR circuit (and its scoped
/// <see cref="CircuitTokenCache"/> instance) across a sign-out + sign-in
/// in the same browser tab. Without the per-call sub-validation, the
/// new user's API calls inherited the previous user's bearer because
/// <c>_accessToken</c> outlived the cookie change. The 401-on-stale
/// recovery path doesn't catch this because the previous bearer is
/// still cryptographically valid — the WebApi denies on the new user's
/// privileges, returning 403, not 401.
///
/// <para>
/// These tests drive <see cref="CircuitTokenCache.GetAccessTokenAsync"/>
/// against a faked <c>IAuthenticationService</c> so the cookie-ticket
/// read returns whatever the test stages, then mutates the simulated
/// <c>HttpContext.User</c> to model the sign-out + sign-in transition.
/// </para>
/// </summary>
public sealed class CircuitTokenCacheTests
{
    private const string SubA = "11111111-1111-1111-1111-111111111111";
    private const string SubB = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task First_call_returns_token_from_cookie_ticket()
    {
        var fakeAuth = new FakeAuthenticationService();
        var http = BuildHttpContext(SubA, fakeAuth);
        fakeAuth.SetTicket(SubA, accessToken: "token-A");

        var cache = NewCache(http);

        var token = await cache.GetAccessTokenAsync();

        Assert.Equal("token-A", token);
        Assert.Equal(1, fakeAuth.AuthenticateCalls);
    }

    [Fact]
    public async Task Repeat_call_with_unchanged_principal_serves_from_cache()
    {
        var fakeAuth = new FakeAuthenticationService();
        var http = BuildHttpContext(SubA, fakeAuth);
        fakeAuth.SetTicket(SubA, accessToken: "token-A");

        var cache = NewCache(http);

        await cache.GetAccessTokenAsync();
        await cache.GetAccessTokenAsync();
        var token = await cache.GetAccessTokenAsync();

        Assert.Equal("token-A", token);
        // First read populated the cache; subsequent reads must NOT
        // re-authenticate the cookie. If this regresses, every API hop
        // pays AuthenticateAsync — measurable on long pages.
        Assert.Equal(1, fakeAuth.AuthenticateCalls);
    }

    [Fact]
    public async Task Principal_change_invalidates_cache_and_serves_new_users_token()
    {
        // Bug G regression: same circuit, principal swap mid-flight.
        // Without the sub validation in GetAccessTokenAsync the second
        // call would return "token-A" — the previous user's bearer.
        var fakeAuth = new FakeAuthenticationService();
        var http = BuildHttpContext(SubA, fakeAuth);
        fakeAuth.SetTicket(SubA, accessToken: "token-A");

        var cache = NewCache(http);

        var firstToken = await cache.GetAccessTokenAsync();
        Assert.Equal("token-A", firstToken);

        // Simulate sign-out + sign-in within the same circuit: the cookie
        // principal flips to a new user, and a fresh ticket lands in the
        // cookie store. Both are observable through the same accessor.
        SetUser(http, SubB);
        fakeAuth.SetTicket(SubB, accessToken: "token-B");

        var secondToken = await cache.GetAccessTokenAsync();

        Assert.Equal("token-B", secondToken);
        // Auth was re-read because the cache invalidated on sub mismatch.
        Assert.Equal(2, fakeAuth.AuthenticateCalls);
    }

    [Fact]
    public async Task Sign_out_to_anonymous_invalidates_cache()
    {
        // After sign-out the cookie principal goes anonymous. The next
        // outbound API call shouldn't reuse the previous user's bearer
        // — even briefly, before they sign back in.
        var fakeAuth = new FakeAuthenticationService();
        var http = BuildHttpContext(SubA, fakeAuth);
        fakeAuth.SetTicket(SubA, accessToken: "token-A");

        var cache = NewCache(http);

        await cache.GetAccessTokenAsync();

        // Anonymous principal has no sub. Cookie auth fails on the
        // re-read, so the cache lands on null (no token).
        SetUser(http, sub: null);
        fakeAuth.ClearTicket();

        var token = await cache.GetAccessTokenAsync();

        Assert.Null(token);
        Assert.Equal(2, fakeAuth.AuthenticateCalls);
    }

    [Fact]
    public async Task Same_principal_multiple_calls_then_change_only_re_authenticates_once()
    {
        // Property: each principal change costs exactly one
        // AuthenticateAsync call; subsequent reads ride the cache.
        var fakeAuth = new FakeAuthenticationService();
        var http = BuildHttpContext(SubA, fakeAuth);
        fakeAuth.SetTicket(SubA, accessToken: "token-A");

        var cache = NewCache(http);

        await cache.GetAccessTokenAsync();   // populate from sub=A
        await cache.GetAccessTokenAsync();   // cached
        Assert.Equal(1, fakeAuth.AuthenticateCalls);

        SetUser(http, SubB);
        fakeAuth.SetTicket(SubB, accessToken: "token-B");

        await cache.GetAccessTokenAsync();   // re-populate from sub=B
        await cache.GetAccessTokenAsync();   // cached
        await cache.GetAccessTokenAsync();   // cached

        Assert.Equal(2, fakeAuth.AuthenticateCalls);
    }

    [Fact]
    public async Task Explicit_invalidate_forces_re_read()
    {
        // BearerTokenHandler calls Invalidate() on a 401 from the
        // WebApi. The next read must re-authenticate even though the
        // principal hasn't changed.
        var fakeAuth = new FakeAuthenticationService();
        var http = BuildHttpContext(SubA, fakeAuth);
        fakeAuth.SetTicket(SubA, accessToken: "token-A");

        var cache = NewCache(http);

        await cache.GetAccessTokenAsync();
        Assert.Equal(1, fakeAuth.AuthenticateCalls);

        cache.Invalidate();

        await cache.GetAccessTokenAsync();
        Assert.Equal(2, fakeAuth.AuthenticateCalls);
    }

    // -------- harness ----------

    private static CircuitTokenCache NewCache(HttpContext http)
    {
        var accessor = new TestHttpContextAccessor(http);
        var configuration = new ConfigurationBuilder().Build();
        // CircuitTokenCache only consults IHttpClientFactory inside
        // TryRefreshAsync, which fires when a cookie's access_token is
        // already past expiry. Tests stage tokens with expires_at far
        // in the future so the refresh path never runs — a null factory
        // is therefore safe for these scenarios. If a future test
        // covers the refresh path, swap in a fake factory.
        return new CircuitTokenCache(
            accessor,
            httpClientFactory: null!,
            configuration,
            NullLogger<CircuitTokenCache>.Instance);
    }

    private static HttpContext BuildHttpContext(string? sub, FakeAuthenticationService fakeAuth)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(fakeAuth);
        var sp = services.BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = sp };
        SetUser(http, sub);
        return http;
    }

    private static void SetUser(HttpContext http, string? sub)
    {
        if (sub is null)
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity());
            return;
        }
        var identity = new ClaimsIdentity(authenticationType: "test");
        identity.AddClaim(new Claim("sub", sub));
        http.User = new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Hand-rolled <see cref="IHttpContextAccessor"/> wrapper. The
    /// production accessor's AsyncLocal storage isn't relevant here —
    /// the cache only reads the property.
    /// </summary>
    private sealed class TestHttpContextAccessor(HttpContext http) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => http; set { /* unused */ } }
    }

    /// <summary>
    /// Hand-rolled <see cref="IAuthenticationService"/>. Returns canned
    /// tickets and counts authenticate calls so tests can assert the
    /// cache short-circuits on hits and re-reads on principal changes.
    /// Following the project's "no Moq / NSubstitute" convention.
    /// </summary>
    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        private string? _sub;
        private string? _accessToken;

        public int AuthenticateCalls { get; private set; }

        public void SetTicket(string sub, string accessToken)
        {
            _sub         = sub;
            _accessToken = accessToken;
        }

        public void ClearTicket()
        {
            _sub         = null;
            _accessToken = null;
        }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            AuthenticateCalls++;

            if (!string.Equals(scheme, CookieAuthenticationDefaults.AuthenticationScheme, StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());

            if (_sub is null || _accessToken is null)
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(authenticationType: "test");
            identity.AddClaim(new Claim("sub", _sub));
            var principal = new ClaimsPrincipal(identity);

            var props = new AuthenticationProperties();
            props.StoreTokens(
            [
                new AuthenticationToken { Name = "access_token", Value = _accessToken },
                // Far-future expiry — keeps the cache on the
                // fresh-token branch and skips TryRefreshAsync, which
                // would need a real IHttpClientFactory to satisfy.
                new AuthenticationToken
                {
                    Name  = "expires_at",
                    Value = DateTimeOffset.UtcNow.AddHours(1).ToString("o"),
                },
            ]);

            var ticket = new AuthenticationTicket(principal, props, scheme!);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        // The cache only consumes AuthenticateAsync; the rest of the
        // contract is unused. Throwing makes accidental coupling loud.
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => throw new NotSupportedException();
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => throw new NotSupportedException();
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
            => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => throw new NotSupportedException();
    }
}
