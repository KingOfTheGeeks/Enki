using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Identity;

namespace SDI.Enki.BlazorServer.Auth;

/// <summary>
/// Circuit-scoped resolver for the effective <see cref="UnitSystem"/> on a
/// page. Combines the Job's preset (passed in by the caller) with the
/// signed-in user's <c>PreferredUnitSystem</c> override fetched from
/// <c>GET /me/preferences</c>.
///
/// <para>
/// Per the AccountSettings hint copy: <i>"Overrides the Job's preset for
/// display only. Storage is always canonical SI."</i> So when a user has
/// a preferred unit set, every display surface picks it up; when they
/// don't, the Job's preset is used.
/// </para>
///
/// <para>
/// Registered as scoped in <c>Program.cs</c> — one instance per Blazor
/// circuit. The fetched preference is cached for the lifetime of the
/// circuit (typically one signed-in browser tab). When the user updates
/// their preference on AccountSettings, that page calls
/// <see cref="Invalidate"/> after the successful PUT so the next page
/// load picks up the new value without a sign-out / sign-in cycle.
/// </para>
///
/// <para>
/// A <see cref="ResolveAsync(string)"/> failure (network, 401, malformed
/// response) falls through to the Job's preset rather than throwing —
/// units are display-only, and a chart that renders in the wrong unit is
/// strictly better than a chart that fails to render at all. The failure
/// is logged at Warning level so it surfaces in dev / ops dashboards.
/// </para>
/// </summary>
public sealed class UnitPreferenceProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<UnitPreferenceProvider> logger)
{
    private string? _cachedPreferredName;
    private bool    _fetched;

    /// <summary>
    /// Returns the effective <see cref="UnitSystem"/> for display: the
    /// user's preferred unit if set, otherwise the Job's preset (parsed
    /// via <see cref="UnitSystem.FromNameOrSi(string?)"/>, which falls
    /// back to SI on an unknown name).
    /// </summary>
    public async Task<UnitSystem> ResolveAsync(string? jobUnitSystemName)
    {
        if (!_fetched) await FetchAsync();

        return _cachedPreferredName is { Length: > 0 } pref
            ? UnitSystem.FromNameOrSi(pref)
            : UnitSystem.FromNameOrSi(jobUnitSystemName);
    }

    /// <summary>
    /// Drop the cached preference so the next <see cref="ResolveAsync"/>
    /// re-fetches from the server. AccountSettings calls this after a
    /// successful save so the user sees the change immediately on the
    /// next page they navigate to.
    /// </summary>
    public void Invalidate()
    {
        _fetched = false;
        _cachedPreferredName = null;
    }

    private async Task FetchAsync()
    {
        try
        {
            var client = httpClientFactory.CreateClient("EnkiIdentity");
            var result = await client.GetAsync<UserPreferencesDto>("me/preferences");
            _cachedPreferredName = result.IsSuccess ? result.Value.PreferredUnitSystem : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not fetch /me/preferences; falling back to the Job's preset for display.");
            _cachedPreferredName = null;
        }
        finally
        {
            _fetched = true;
        }
    }
}
