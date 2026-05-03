using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SDI.Enki.BlazorServer.Api;

namespace SDI.Enki.BlazorServer.Tests.Api;

/// <summary>
/// Coverage for the API result envelope plumbing — every status-code
/// branch + RFC 7807 parse + non-RFC body fallback + network failure +
/// 204 No Content path. The fixture pumps HTTP responses through a
/// fake <see cref="HttpMessageHandler"/>; no real network or server.
/// </summary>
public class HttpClientApiExtensionsTests
{
    /// <summary>
    /// Hand-rolled <see cref="HttpMessageHandler"/> stub. Each test
    /// stages the response (status, content) it wants and the
    /// extensions methods talk to it through a normal
    /// <see cref="HttpClient"/>.
    /// </summary>
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await responder(request);
        }
    }

    private static HttpClient NewClient(StubHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://test/") };

    private static StubHandler RespondsWith(HttpStatusCode status, string? json = null, string? mediaType = null) =>
        new(_ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = json is null
                ? new ByteArrayContent([])
                : new StringContent(json, Encoding.UTF8, mediaType ?? "application/json"),
        }));

    private static StubHandler ThrowsNetwork() =>
        new(_ => throw new HttpRequestException("connection reset"));

    private sealed record SampleDto(int Id, string Name);

    // ---------- GET happy paths ----------

    [Fact]
    public async Task GetAsync_200WithJsonBody_ParsesAndReturnsOk()
    {
        var handler = RespondsWith(HttpStatusCode.OK,
            JsonSerializer.Serialize(new { id = 7, name = "Eagle Ford" }));
        var http = NewClient(handler);

        var result = await http.GetAsync<SampleDto>("things/7");

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value.Id);
        Assert.Equal("Eagle Ford", result.Value.Name);
    }

    [Fact]
    public async Task GetAsync_200WithEmptyBody_FailsAsUnknown()
    {
        var handler = RespondsWith(HttpStatusCode.OK, "null");
        var http = NewClient(handler);

        var result = await http.GetAsync<SampleDto>("things/1");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.Unknown, result.Error.Kind);
    }

    // ---------- error-status mappings ----------

    [Theory]
    [InlineData((int)HttpStatusCode.BadRequest,           ApiErrorKind.Validation)]
    [InlineData((int)HttpStatusCode.Unauthorized,         ApiErrorKind.Unauthenticated)]
    [InlineData((int)HttpStatusCode.Forbidden,            ApiErrorKind.Forbidden)]
    [InlineData((int)HttpStatusCode.NotFound,             ApiErrorKind.NotFound)]
    [InlineData((int)HttpStatusCode.RequestTimeout,       ApiErrorKind.Timeout)]
    [InlineData((int)HttpStatusCode.GatewayTimeout,       ApiErrorKind.Timeout)]
    [InlineData((int)HttpStatusCode.Conflict,             ApiErrorKind.Conflict)]
    [InlineData((int)HttpStatusCode.TooManyRequests,      ApiErrorKind.RateLimited)]
    [InlineData((int)HttpStatusCode.InternalServerError,  ApiErrorKind.Server)]
    [InlineData((int)HttpStatusCode.BadGateway,           ApiErrorKind.Server)]
    [InlineData((int)HttpStatusCode.ServiceUnavailable,   ApiErrorKind.Server)]
    public async Task GetAsync_StatusToKindMapping(int status, ApiErrorKind expectedKind)
    {
        var handler = RespondsWith((HttpStatusCode)status);
        var http = NewClient(handler);

        var result = await http.GetAsync<SampleDto>("things");

        Assert.False(result.IsSuccess);
        Assert.Equal(status, result.Error.StatusCode);
        Assert.Equal(expectedKind, result.Error.Kind);
    }

    [Fact]
    public async Task GetAsync_400ValidationProblemDetails_ParsesFieldErrors()
    {
        var body = JsonSerializer.Serialize(new
        {
            title = "Validation",
            status = 400,
            errors = new Dictionary<string, string[]>
            {
                ["FromMeasured"] = new[] { "From must be ≤ To." },
                ["Name"]         = new[] { "Required." },
            },
        });
        var handler = RespondsWith(HttpStatusCode.BadRequest, body);
        var http = NewClient(handler);

        var result = await http.GetAsync<SampleDto>("things");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.Validation, result.Error.Kind);
        Assert.NotNull(result.Error.FieldErrors);
        Assert.Contains("FromMeasured", result.Error.FieldErrors!.Keys);
        Assert.Contains("From must be ≤ To.", result.Error.FieldErrors["FromMeasured"]);
    }

    [Fact]
    public async Task GetAsync_409ProblemDetailsWithExtensions_ForwardsExtensions()
    {
        var body = JsonSerializer.Serialize(new
        {
            title = "Conflict",
            status = 409,
            detail = "tie-on overwrite",
            conflictKind = "tieOnOverwrite",
            existingTieOn = new { Depth = 0.0 },
        });
        var handler = RespondsWith(HttpStatusCode.Conflict, body);
        var http = NewClient(handler);

        var result = await http.GetAsync<SampleDto>("things");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.Conflict, result.Error.Kind);
        Assert.Equal("Conflict", result.Error.Title);
        Assert.NotNull(result.Error.Extensions);
        Assert.Contains("conflictKind", result.Error.Extensions!.Keys);
    }

    [Fact]
    public async Task GetAsync_500NonJsonBody_FallsBackToBodyPreview()
    {
        var handler = RespondsWith(HttpStatusCode.InternalServerError,
            "<html><body>nginx 502</body></html>",
            mediaType: "text/html");
        var http = NewClient(handler);

        var result = await http.GetAsync<SampleDto>("things");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.Server, result.Error.Kind);
        Assert.Contains("nginx", result.Error.Detail ?? "");
    }

    [Fact]
    public async Task GetAsync_NetworkException_ReturnsNetworkKind()
    {
        var http = NewClient(ThrowsNetwork());

        var result = await http.GetAsync<SampleDto>("things");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.Network, result.Error.Kind);
        Assert.Equal(0, result.Error.StatusCode);
    }

    // ---------- GetBytesAsync ----------

    [Fact]
    public async Task GetBytesAsync_200_ReturnsBytes()
    {
        var bytes = new byte[] { 0x01, 0x02, 0x03 };
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        }));
        var http = NewClient(handler);

        var result = await http.GetBytesAsync("things/1/file");

        Assert.True(result.IsSuccess);
        Assert.Equal(bytes, result.Value);
    }

    [Fact]
    public async Task GetBytesAsync_404_ReturnsNotFoundError()
    {
        var http = NewClient(RespondsWith(HttpStatusCode.NotFound));

        var result = await http.GetBytesAsync("things/1/file");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.NotFound, result.Error.Kind);
    }

    [Fact]
    public async Task GetBytesAsync_NetworkException_ReturnsNetworkKind()
    {
        var http = NewClient(ThrowsNetwork());

        var result = await http.GetBytesAsync("things/1/file");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.Network, result.Error.Kind);
    }

    // ---------- POST / PUT / DELETE void-result paths ----------

    [Fact]
    public async Task PostAsync_204NoContent_ReturnsSuccess()
    {
        var http = NewClient(RespondsWith(HttpStatusCode.NoContent));

        var result = await http.PostAsync("things", new SampleDto(1, "x"));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task PostAsync_201Created_ReturnsSuccess()
    {
        var http = NewClient(RespondsWith(HttpStatusCode.Created, "{\"id\":7,\"name\":\"new\"}"));

        var result = await http.PostAsync("things", new SampleDto(1, "x"));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task PutAsync_204_ReturnsSuccess()
    {
        var http = NewClient(RespondsWith(HttpStatusCode.NoContent));

        var result = await http.PutAsync("things/1", new SampleDto(1, "x"));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task PutAsync_409Conflict_ReturnsConflictError()
    {
        var http = NewClient(RespondsWith(HttpStatusCode.Conflict));

        var result = await http.PutAsync("things/1", new SampleDto(1, "x"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.Conflict, result.Error.Kind);
    }

    [Fact]
    public async Task DeleteAsync_204_ReturnsSuccess()
    {
        var http = NewClient(RespondsWith(HttpStatusCode.NoContent));

        var result = await HttpClientApiExtensions.DeleteAsync(http, "things/1");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteAsync_404_ReturnsNotFoundError()
    {
        var http = NewClient(RespondsWith(HttpStatusCode.NotFound));

        var result = await HttpClientApiExtensions.DeleteAsync(http, "things/1");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.NotFound, result.Error.Kind);
    }

    [Fact]
    public async Task DeleteAsync_NetworkException_ReturnsNetworkKind()
    {
        var http = NewClient(ThrowsNetwork());

        var result = await HttpClientApiExtensions.DeleteAsync(http, "things/1");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorKind.Network, result.Error.Kind);
    }

    // ---------- typed POST returning a body ----------

    [Fact]
    public async Task PostAsync_TReqTResp_ReturnsParsedResponse()
    {
        var handler = RespondsWith(HttpStatusCode.Created, "{\"id\":42,\"name\":\"new\"}");
        var http = NewClient(handler);

        var result = await http.PostAsync<SampleDto, SampleDto>("things", new SampleDto(0, "x"));

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value.Id);
    }

    // ---------- AsAlertText helper ----------

    [Fact]
    public void ApiError_AsAlertText_JoinsTitleAndDetail()
    {
        var err = new ApiError(404, ApiErrorKind.NotFound, "Not found", "Formation 7 missing", null);
        Assert.Equal("Not found — Formation 7 missing", err.AsAlertText());
    }

    [Fact]
    public void ApiError_AsAlertText_OmitsDashWhenDetailIsNull()
    {
        var err = new ApiError(401, ApiErrorKind.Unauthenticated, "Sign in required", null, null);
        Assert.Equal("Sign in required", err.AsAlertText());
    }
}
