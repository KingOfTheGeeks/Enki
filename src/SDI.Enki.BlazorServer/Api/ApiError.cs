namespace SDI.Enki.BlazorServer.Api;

/// <summary>
/// Typed error projection of a <c>ProblemDetails</c> /
/// <c>ValidationProblemDetails</c> response from the Enki WebApi.
/// Pages render <see cref="Title"/> as the headline alert text and
/// <see cref="FieldErrors"/> next to the offending input when the
/// failure is a model-state validation problem (HTTP 400).
///
/// <para>
/// Carries <see cref="StatusCode"/> verbatim so pages can branch on
/// it (401 → re-auth banner, 403 → access-denied banner, 404 →
/// "deleted by another user" prompt, 409 → conflict copy, 429 →
/// "slow down" copy, 5xx → "try again" copy). The companion
/// <see cref="Kind"/> derives a small enum from the status so most
/// pages don't need to reach for the integer.
/// </para>
/// </summary>
/// <param name="StatusCode">
/// HTTP status code from the response (verbatim — 400, 401, 403,
/// 404, 408, 409, 429, 5xx, …). Zero when the failure happened
/// before a response came back (e.g. network exception); use
/// <see cref="Kind"/> to distinguish.
/// </param>
/// <param name="Kind">
/// Coarse category derived from <see cref="StatusCode"/>. Lets a
/// page do a single switch against the kind without memorising the
/// HTTP table.
/// </param>
/// <param name="Title">
/// Short human-readable summary. Comes from the response's
/// <c>title</c> property (RFC 7807) when the body parsed; falls
/// back to a derived message ("Network error", "Unexpected status
/// 500", etc.) when it didn't.
/// </param>
/// <param name="Detail">
/// Longer explanation when the response carried one; <c>null</c>
/// otherwise. Pages can render it as a sub-line under the alert.
/// </param>
/// <param name="FieldErrors">
/// Per-field validation messages from
/// <c>ValidationProblemDetails.errors</c> (only populated when
/// <see cref="StatusCode"/> = 400 and the body parsed as
/// <c>ValidationProblemDetails</c>). Key = field name, value =
/// list of error strings. Pages bind these next to the input
/// they apply to.
/// </param>
/// <param name="Extensions">
/// Extra members carried on the response's RFC 7807 body beyond the
/// standard title / detail / errors / status / type / instance
/// quintet. ASP.NET's <c>ProblemDetails</c> deserialises unknown
/// JSON members into a dictionary via <c>[JsonExtensionData]</c>; we
/// pass that through verbatim so the call site can read structured
/// failure data (e.g. the <c>existingTieOn</c> / <c>importedTieOn</c>
/// objects on the survey-import 409). Values are
/// <c>System.Text.Json.JsonElement</c> for primitives / objects;
/// boxed primitives for the rare path that handles them already.
/// <c>null</c> when the response had no extensions or wasn't
/// ProblemDetails-shaped.
/// </param>
public sealed record ApiError(
    int                                       StatusCode,
    ApiErrorKind                              Kind,
    string                                    Title,
    string?                                   Detail,
    IReadOnlyDictionary<string, string[]>?    FieldErrors,
    IReadOnlyDictionary<string, object?>?     Extensions = null)
{
    /// <summary>
    /// Compact human-readable rendering for an inline alert. Joins
    /// title + detail with an em dash; doesn't include
    /// FieldErrors (those go next to inputs, not in the headline).
    /// </summary>
    public string AsAlertText() =>
        Detail is null ? Title : $"{Title} — {Detail}";
}

/// <summary>
/// Coarse kind of API failure derived from the HTTP status code.
/// One value per "kind of UI message" we want pages to render —
/// not a 1:1 with HTTP, since e.g. 408 (timeout) and 504 are both
/// <see cref="Timeout"/> from the user's POV.
/// </summary>
public enum ApiErrorKind
{
    /// <summary>Generic / unmappable failure.</summary>
    Unknown,
    /// <summary>Network / transport failure before a response arrived.</summary>
    Network,
    /// <summary>HTTP 400 — request body validation failed; check <c>FieldErrors</c>.</summary>
    Validation,
    /// <summary>HTTP 401 — token missing or expired.</summary>
    Unauthenticated,
    /// <summary>HTTP 403 — caller authenticated but lacks tenant / role access.</summary>
    Forbidden,
    /// <summary>HTTP 404 — entity (or its parent) does not exist.</summary>
    NotFound,
    /// <summary>HTTP 408 — request timed out server-side.</summary>
    Timeout,
    /// <summary>HTTP 409 — state-machine conflict (illegal transition, has children, …).</summary>
    Conflict,
    /// <summary>HTTP 429 — rate limit exceeded.</summary>
    RateLimited,
    /// <summary>HTTP 5xx — server-side bug or downstream outage.</summary>
    Server,
}
