using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Core.Units;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Wells;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells/{WellId:int}")]
[Authorize]
public partial class WellDetail : ComponentBase
{
    [Inject] public IHttpClientFactory      HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager       Nav               { get; set; } = default!;
    [Inject] public UnitPreferenceProvider  UnitPrefs         { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public int    WellId     { get; set; }

    private WellDetailDto? _well;
    private MagneticsDto?  _magnetics;
    private UnitSystem     _units = UnitSystem.SI;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");

        // Three independent fetches in parallel: the Job (drives
        // the units cascade), the Well, and the optional per-well
        // magnetic-reference. The magnetics call returning 404 is
        // the "Not set" state — handled by leaving _magnetics null.
        var jobTask  = client.GetAsync<JobDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}");
        var wellTask = client.GetAsync<WellDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}");
        var magTask  = client.GetAsync<MagneticsDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells/{WellId}/magnetics");

        await Task.WhenAll(jobTask, wellTask, magTask);

        var jobResult  = jobTask.Result;
        var wellResult = wellTask.Result;
        var magResult  = magTask.Result;

        _units = await UnitPrefs.ResolveAsync(jobResult.IsSuccess ? jobResult.Value.UnitSystem : null);

        if (!wellResult.IsSuccess)
        {
            _error = wellResult.Error.Kind == ApiErrorKind.NotFound
                ? $"Well {WellId} not found in tenant {TenantCode}."
                : wellResult.Error.AsAlertText();
            return;
        }
        _well = wellResult.Value;

        if (magResult.IsSuccess)
        {
            _magnetics = magResult.Value;
        }
        // 404 → no per-well magnetics; _magnetics stays null and
        // the "Not set" branch renders. Any other failure is
        // non-fatal — surface as null and let the user re-curate.
    }

    private static string TypeClass(string s) => s switch
    {
        "Target"    => "enki-status-active",
        "Intercept" => "enki-status-inactive",
        "Offset"    => "enki-status-archived",
        _           => "",
    };
}
