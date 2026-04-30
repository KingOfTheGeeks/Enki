using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using SDI.Enki.Identity.RateLimiting;

namespace SDI.Enki.Identity.Tests.RateLimiting;

/// <summary>
/// Regression tests for the <c>ConnectEndpoints</c> rate-limiter's 429
/// rejection body (issue #24).
///
/// <para>
/// The failure mode they pin: ASP.NET Core's default rate-limiter
/// rejection writes 429 with an empty body, which makes the OIDC client
/// in BlazorServer crash on response parsing
/// (<c>ArgumentNullException IDX10000</c>) → <c>AuthenticationFailureException</c>
/// → 500 to the user. <see cref="OAuthRateLimitedResponse.WriteAsync"/>
/// fixes that by writing an RFC 6749 §5.2-shaped JSON body.
/// </para>
/// </summary>
public class OAuthRateLimitedResponseTests
{
    [Fact]
    public async Task WriteAsync_Sets429AndWritesOAuthJsonError()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        // OnRejectedContext is sealed in modern .NET — we can construct one
        // with the HttpContext alone (the Lease property is for the rate
        // limiter internals, not consumed by our handler).
        var rejected = new OnRejectedContext { HttpContext = ctx, Lease = new StubLease() };

        await OAuthRateLimitedResponse.WriteAsync(rejected, CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, ctx.Response.StatusCode);
        // WriteAsJsonAsync appends "; charset=utf-8" — match by prefix so
        // the test doesn't bind to a framework-controlled detail.
        Assert.StartsWith("application/json", ctx.Response.ContentType);

        ctx.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);

        // RFC 6749 §5.2 shape: at minimum an "error" string. We also ship
        // an "error_description" so downstream clients can surface
        // something user-actionable. Both must be present and non-empty
        // so the OIDC handler's JSON parser is happy.
        Assert.True(doc.RootElement.TryGetProperty("error", out var errorEl));
        Assert.Equal("temporarily_unavailable", errorEl.GetString());

        Assert.True(doc.RootElement.TryGetProperty("error_description", out var descEl));
        Assert.False(string.IsNullOrWhiteSpace(descEl.GetString()),
            "error_description must be non-empty so the OAuth client surfaces it.");
    }

    [Fact]
    public async Task WriteAsync_ProducesParseableBody_ForOidcClientPathThatTrippedIdx10000()
    {
        // Issue #24 root cause: BlazorServer's OIDC handler hands the
        // response body to Microsoft.IdentityModel's JSON parser, which
        // throws "IDX10000: parameter 'json' cannot be null or empty"
        // on an empty body. This test is the contract: whatever 429 body
        // we ship MUST round-trip through System.Text.Json.JsonDocument
        // (the same family of parser the IdentityModel stack uses) without
        // throwing.
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var rejected = new OnRejectedContext { HttpContext = ctx, Lease = new StubLease() };

        await OAuthRateLimitedResponse.WriteAsync(rejected, CancellationToken.None);

        ctx.Response.Body.Position = 0;
        var raw = await new StreamReader(ctx.Response.Body).ReadToEndAsync();

        Assert.False(string.IsNullOrWhiteSpace(raw),
            "Body must be non-empty — that's the whole reason for the OnRejected handler.");

        // No throw == contract held.
        var parsed = JsonDocument.Parse(raw);
        Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
    }

    /// <summary>
    /// Minimal <see cref="RateLimitLease"/> stub for satisfying
    /// <see cref="OnRejectedContext.Lease"/> in tests. The handler under
    /// test never reads the lease — it only writes to
    /// <c>HttpContext.Response</c> — so this stays as terse as the
    /// abstract API allows.
    /// </summary>
    private sealed class StubLease : RateLimitLease
    {
        public override bool IsAcquired => false;
        public override IEnumerable<string> MetadataNames => Array.Empty<string>();
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
        protected override void Dispose(bool disposing) { /* nothing to release */ }
    }
}
