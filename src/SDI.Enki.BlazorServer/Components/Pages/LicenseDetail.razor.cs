using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Authorization;
using SDI.Enki.Shared.Licensing;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/licenses/{Id:guid}")]
[Authorize(Policy = EnkiPolicies.CanManageLicensing)]
public partial class LicenseDetail : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;
    [Inject] public IJSRuntime         JS                { get; set; } = default!;

    [Parameter] public Guid Id { get; set; }

    private LicenseDetailDto? _license;
    private string? _error;
    private bool _busy;

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<LicenseDetailDto>($"licenses/{Id}");
        if (!result.IsSuccess)
        {
            _error = result.Error.Kind == ApiErrorKind.NotFound
                ? "License not found."
                : result.Error.AsAlertText();
            return;
        }
        _license = result.Value;
    }

    private async Task RevokeAsync()
    {
        var reason = await JS.InvokeAsync<string?>("prompt", "Revocation reason (required):");
        if (string.IsNullOrWhiteSpace(reason)) return;

        _busy = true;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await client.PostAsync($"licenses/{Id}/revoke", new RevokeLicenseDto(reason));
            if (!result.IsSuccess) { _error = result.Error.AsAlertText(); return; }

            // Re-fetch detail to refresh status pill + reason fields.
            var refresh = await client.GetAsync<LicenseDetailDto>($"licenses/{Id}");
            if (refresh.IsSuccess) _license = refresh.Value;
        }
        finally
        {
            _busy = false;
        }
    }

    private static IEnumerable<(string Label, bool On)> EnumerateFeatures(LicenseFeaturesDto f)
    {
        yield return ("Warrior",         f.AllowWarrior);
        yield return ("North Sea",       f.AllowNorthSea);
        yield return ("Serial",          f.AllowSerial);
        yield return ("Rotary",          f.AllowRotary);
        yield return ("Gradient",        f.AllowGradient);
        yield return ("Passive",         f.AllowPassive);
        yield return ("Warrior logging", f.AllowWarriorLogging);
        yield return ("Calibrate",       f.AllowCalibrate);
        yield return ("Survey",          f.AllowSurvey);
        yield return ("Results",         f.AllowResults);
        yield return ("Gyro",            f.AllowGyro);
    }

    private static string StatusClass(string s) => s switch
    {
        "Active"  => "enki-status-active",
        "Revoked" => "enki-status-inactive",
        "Expired" => "enki-status-archived",
        _         => "",
    };
}
