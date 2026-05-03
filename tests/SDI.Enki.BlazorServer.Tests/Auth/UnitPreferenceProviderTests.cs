using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Core.Units;

namespace SDI.Enki.BlazorServer.Tests.Auth;

/// <summary>
/// Coverage for <see cref="UnitPreferenceProvider"/> — circuit-scoped
/// resolver for the user's effective <see cref="UnitSystem"/>. The
/// fetch goes to <c>GET /me/preferences</c> on the Identity host;
/// the user's override (if any) wins over the Job's preset, and a
/// fetch failure must fall back to the Job's preset (display-only,
/// never throw).
/// </summary>
public class UnitPreferenceProviderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return await responder(request);
        }
    }

    private sealed class FixedClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private static UnitPreferenceProvider NewSut(StubHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://identity/") };
        return new UnitPreferenceProvider(
            new FixedClientFactory(http),
            NullLogger<UnitPreferenceProvider>.Instance);
    }

    private static StubHandler RespondsWithPreference(string? preferredName)
    {
        var json = JsonSerializer.Serialize(new { preferredUnitSystem = preferredName });
        return new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }));
    }

    [Fact]
    public async Task ResolveAsync_UserHasPreference_OverridesJobPreset()
    {
        var sut = NewSut(RespondsWithPreference("Field"));

        var result = await sut.ResolveAsync(jobUnitSystemName: UnitSystem.Metric.Name);

        Assert.Equal(UnitSystem.Field, result);
    }

    [Fact]
    public async Task ResolveAsync_UserPreferenceNull_UsesJobPreset()
    {
        var sut = NewSut(RespondsWithPreference(null));

        var result = await sut.ResolveAsync(jobUnitSystemName: UnitSystem.Metric.Name);

        Assert.Equal(UnitSystem.Metric, result);
    }

    [Fact]
    public async Task ResolveAsync_UnknownJobPresetWithNoPreference_FallsBackToSi()
    {
        var sut = NewSut(RespondsWithPreference(null));

        var result = await sut.ResolveAsync(jobUnitSystemName: "Bogus");

        Assert.Equal(UnitSystem.SI, result);
    }

    [Fact]
    public async Task ResolveAsync_FetchFails_FallsBackToJobPreset()
    {
        // 500 from Identity → ResolveAsync swallows + logs + falls
        // through to the Job preset.
        var handler = new StubHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var sut = NewSut(handler);

        var result = await sut.ResolveAsync(jobUnitSystemName: UnitSystem.Field.Name);

        Assert.Equal(UnitSystem.Field, result);
    }

    [Fact]
    public async Task ResolveAsync_NetworkException_FallsBackToJobPreset()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("DNS fail"));
        var sut = NewSut(handler);

        var result = await sut.ResolveAsync(jobUnitSystemName: UnitSystem.Field.Name);

        Assert.Equal(UnitSystem.Field, result);
    }

    [Fact]
    public async Task ResolveAsync_CalledTwice_FetchesOnceAndCachesResult()
    {
        var handler = RespondsWithPreference("Field");
        var sut = NewSut(handler);

        await sut.ResolveAsync(UnitSystem.Metric.Name);
        await sut.ResolveAsync(UnitSystem.Metric.Name);

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Invalidate_ForcesRefetchOnNextResolve()
    {
        var handler = RespondsWithPreference("Field");
        var sut = NewSut(handler);

        await sut.ResolveAsync(UnitSystem.Metric.Name);
        Assert.Equal(1, handler.CallCount);

        sut.Invalidate();
        await sut.ResolveAsync(UnitSystem.Metric.Name);

        Assert.Equal(2, handler.CallCount);
    }
}
