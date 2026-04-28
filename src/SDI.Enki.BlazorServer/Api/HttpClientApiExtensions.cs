using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace SDI.Enki.BlazorServer.Api;

/// <summary>
/// Bridges a raw <see cref="HttpClient"/> call into the typed
/// <see cref="ApiResult"/> / <see cref="ApiResult{T}"/> envelope
/// the rest of the Blazor pages consume. Centralises the
/// <c>ProblemDetails</c> / <c>ValidationProblemDetails</c> parsing
/// so individual pages don't end up with raw response-body strings
/// in their <c>_error</c> alerts.
///
/// <para>
/// Handles three failure shapes consistently:
/// <list type="bullet">
///   <item><b>Network / transport</b>: <c>HttpRequestException</c>
///   thrown before any response arrived → <see cref="ApiErrorKind.Network"/>.</item>
///   <item><b>RFC 7807 ProblemDetails / ValidationProblemDetails</b>:
///   the WebApi's standard error shape → status-derived kind +
///   parsed <c>title</c> / <c>detail</c> / <c>errors</c> map.</item>
///   <item><b>Non-RFC error responses</b> (rare; e.g. an
///   intermediate proxy returning HTML): falls back to a synthetic
///   <see cref="ApiError"/> built from the status code + body
///   preview.</item>
/// </list>
/// </para>
///
/// <para>
/// 204 No Content is a successful PUT/DELETE; the typed-result
/// helpers treat it as <see cref="ApiResult{T}.Ok"/> with
/// <c>default</c> Value, which is fine for the void overloads
/// where <c>T</c> is unused.
/// </para>
/// </summary>
internal static class HttpClientApiExtensions
{
    /// <summary>
    /// JSON options that match ASP.NET Core's defaults for
    /// <c>ProblemDetails</c> serialisation (camelCase property names).
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // ---------- GET ----------

    public static async Task<ApiResult<T>> GetAsync<T>(
        this HttpClient http,
        string path,
        CancellationToken ct = default)
        => await SendAsApiResultAsync<T>(http, () => http.GetAsync(path, ct), ct);

    // ---------- POST ----------

    public static async Task<ApiResult<TResp>> PostAsync<TReq, TResp>(
        this HttpClient http,
        string path,
        TReq body,
        CancellationToken ct = default)
        => await SendAsApiResultAsync<TResp>(
            http,
            () => http.PostAsJsonAsync(path, body, JsonOptions, ct),
            ct);

    public static async Task<ApiResult> PostAsync<TReq>(
        this HttpClient http,
        string path,
        TReq body,
        CancellationToken ct = default)
        => await SendAsApiResultAsync(
            http,
            () => http.PostAsJsonAsync(path, body, JsonOptions, ct),
            ct);

    public static async Task<ApiResult> PostAsync(
        this HttpClient http,
        string path,
        CancellationToken ct = default)
        => await SendAsApiResultAsync(
            http,
            () => http.PostAsync(path, content: null, ct),
            ct);

    // ---------- PUT ----------

    public static async Task<ApiResult> PutAsync<TReq>(
        this HttpClient http,
        string path,
        TReq body,
        CancellationToken ct = default)
        => await SendAsApiResultAsync(
            http,
            () => http.PutAsJsonAsync(path, body, JsonOptions, ct),
            ct);

    // ---------- DELETE ----------

    public static async Task<ApiResult> DeleteAsync(
        this HttpClient http,
        string path,
        CancellationToken ct = default)
        => await SendAsApiResultAsync(
            http,
            () => http.DeleteAsync(path, ct),
            ct);

    // ---------- core dispatch ----------

    /// <summary>
    /// Run the supplied request closure, then map status + body
    /// into either <see cref="ApiResult{T}.Ok"/> with a parsed
    /// payload, or <see cref="ApiResult{T}.Failure"/> with a
    /// parsed <see cref="ApiError"/>. <see cref="HttpRequestException"/>
    /// is the only exception caught — anything else (cancellation,
    /// programmer error in the closure) propagates to the caller.
    /// </summary>
    private static async Task<ApiResult<T>> SendAsApiResultAsync<T>(
        HttpClient http,
        Func<Task<HttpResponseMessage>> send,
        CancellationToken ct)
    {
        HttpResponseMessage? resp = null;
        try
        {
            resp = await send();
            if (resp.IsSuccessStatusCode)
            {
                if (resp.StatusCode == HttpStatusCode.NoContent)
                    return ApiResult<T>.Ok(default!);

                var value = await resp.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
                return value is null
                    ? ApiResult<T>.Failure(new ApiError(
                        StatusCode:  (int)resp.StatusCode,
                        Kind:        ApiErrorKind.Unknown,
                        Title:       "Empty response body where one was expected.",
                        Detail:      null,
                        FieldErrors: null))
                    : ApiResult<T>.Ok(value);
            }

            return ApiResult<T>.Failure(await ReadErrorAsync(resp, ct));
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<T>.Failure(new ApiError(
                StatusCode:  0,
                Kind:        ApiErrorKind.Network,
                Title:       "Could not reach the server.",
                Detail:      ex.Message,
                FieldErrors: null));
        }
        finally
        {
            resp?.Dispose();
        }
    }

    /// <summary>
    /// Void-result variant. Same dispatch semantics as the typed
    /// overload; success returns <see cref="ApiResult.Success"/>
    /// regardless of what the body was (intended for 204 No Content
    /// and 201 Created where the location header is what callers
    /// care about).
    /// </summary>
    private static async Task<ApiResult> SendAsApiResultAsync(
        HttpClient http,
        Func<Task<HttpResponseMessage>> send,
        CancellationToken ct)
    {
        HttpResponseMessage? resp = null;
        try
        {
            resp = await send();
            if (resp.IsSuccessStatusCode) return ApiResult.Success;
            return ApiResult.Failure(await ReadErrorAsync(resp, ct));
        }
        catch (HttpRequestException ex)
        {
            return ApiResult.Failure(new ApiError(
                StatusCode:  0,
                Kind:        ApiErrorKind.Network,
                Title:       "Could not reach the server.",
                Detail:      ex.Message,
                FieldErrors: null));
        }
        finally
        {
            resp?.Dispose();
        }
    }

    /// <summary>
    /// Pull a <see cref="ProblemDetails"/> /
    /// <see cref="ValidationProblemDetails"/> off the failed
    /// response. ASP.NET Core's default error shape is RFC 7807;
    /// we try ValidationProblemDetails first because it's a
    /// superset (carries an <c>errors</c> dictionary), then
    /// fall back to ProblemDetails for non-validation 4xx / 5xx.
    /// If neither parses (e.g. a proxy returned HTML), we build
    /// a synthetic <see cref="ApiError"/> from the status + body
    /// preview so the user still sees something useful.
    /// </summary>
    private static async Task<ApiError> ReadErrorAsync(
        HttpResponseMessage resp,
        CancellationToken ct)
    {
        var status = (int)resp.StatusCode;
        var kind   = StatusToKind(status);

        // Try the structured RFC 7807 shape first.
        try
        {
            // ValidationProblemDetails extends ProblemDetails with
            // an `errors` dictionary; if the response is plain
            // ProblemDetails the dictionary just stays empty.
            var vpd = await resp.Content.ReadFromJsonAsync<ValidationProblemDetails>(JsonOptions, ct);
            if (vpd is not null && (!string.IsNullOrEmpty(vpd.Title) || vpd.Errors.Count > 0))
            {
                IReadOnlyDictionary<string, string[]>? fieldErrors = vpd.Errors.Count > 0
                    ? new Dictionary<string, string[]>(vpd.Errors)
                    : null;

                return new ApiError(
                    StatusCode:  status,
                    Kind:        kind,
                    Title:       vpd.Title  ?? DefaultTitleFor(kind),
                    Detail:      vpd.Detail,
                    FieldErrors: fieldErrors);
            }
        }
        catch (JsonException) { /* fall through to the body-preview path */ }
        catch (NotSupportedException) { /* content type wasn't JSON */ }

        // Body wasn't JSON or didn't match the shape — fall back to
        // a body preview so users at least see SOMETHING actionable.
        string? preview = null;
        try
        {
            var raw = await resp.Content.ReadAsStringAsync(ct);
            preview = raw.Length > 240 ? raw[..240] + "…" : raw;
            if (string.IsNullOrWhiteSpace(preview)) preview = null;
        }
        catch { /* leave preview null */ }

        return new ApiError(
            StatusCode:  status,
            Kind:        kind,
            Title:       DefaultTitleFor(kind),
            Detail:      preview,
            FieldErrors: null);
    }

    private static ApiErrorKind StatusToKind(int status) => status switch
    {
        400 => ApiErrorKind.Validation,
        401 => ApiErrorKind.Unauthenticated,
        403 => ApiErrorKind.Forbidden,
        404 => ApiErrorKind.NotFound,
        408 or 504 => ApiErrorKind.Timeout,
        409 => ApiErrorKind.Conflict,
        429 => ApiErrorKind.RateLimited,
        >= 500 => ApiErrorKind.Server,
        _      => ApiErrorKind.Unknown,
    };

    private static string DefaultTitleFor(ApiErrorKind kind) => kind switch
    {
        ApiErrorKind.Validation      => "The request was rejected as invalid.",
        ApiErrorKind.Unauthenticated => "You're not signed in.",
        ApiErrorKind.Forbidden       => "You don't have access to this resource.",
        ApiErrorKind.NotFound        => "Not found.",
        ApiErrorKind.Timeout         => "The request timed out.",
        ApiErrorKind.Conflict        => "The request conflicts with the current state.",
        ApiErrorKind.RateLimited     => "Too many requests. Please slow down.",
        ApiErrorKind.Server          => "The server hit an unexpected error.",
        ApiErrorKind.Network         => "Could not reach the server.",
        _                            => "The request failed.",
    };
}
