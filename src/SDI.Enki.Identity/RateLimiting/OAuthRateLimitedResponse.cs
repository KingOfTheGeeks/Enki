using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace SDI.Enki.Identity.RateLimiting;

/// <summary>
/// Shapes 429 responses from the <c>ConnectEndpoints</c> rate limiter
/// into an OAuth 2.0–compliant JSON error body
/// (<c>{"error":"…","error_description":"…"}</c> per RFC 6749 §5.2).
///
/// <para>
/// <b>Why this exists:</b> ASP.NET Core's default rate-limiter rejection
/// writes 429 with an empty body. Microsoft.IdentityModel's OIDC client
/// hands the response body to its JSON parser unconditionally, and an
/// empty string trips <c>ArgumentNullException IDX10000</c> ("the
/// parameter 'json' cannot be a 'null' or an empty string"). The .NET
/// OIDC handler wraps it in <c>AuthenticationFailureException</c>, which
/// surfaces as a 500 to the user — the reproduction signature on issue
/// #24 (rapid sign-in/out cycles → /connect/token returns 429 → BlazorServer
/// /signin-oidc throws on response parsing).
/// </para>
///
/// <para>
/// Wiring this into <c>RateLimiterOptions.OnRejected</c> swaps the
/// empty body for a parseable JSON envelope. The OIDC client still
/// surfaces the failure (it can't sign the user in without a token),
/// but it surfaces a clean "temporarily unavailable" error instead of
/// crashing the request.
/// </para>
/// </summary>
internal static class OAuthRateLimitedResponse
{
    /// <summary>
    /// <see cref="RateLimiterOptions.OnRejected"/>-compatible delegate that
    /// writes the OAuth-shaped 429 body. Extracted from the inline lambda
    /// in <c>Program.cs</c> so it can be unit-tested without spinning up
    /// the full Identity host.
    /// </summary>
    public static async ValueTask WriteAsync(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error             = "temporarily_unavailable",
            error_description = "Too many requests to the Identity server. " +
                                "Please retry shortly.",
        }, cancellationToken);
    }
}
