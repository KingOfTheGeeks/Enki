using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Shots;

namespace SDI.Enki.BlazorServer.Components.Pages.Runs;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/runs/{RunId:guid}/shots")]
[Authorize]
public partial class Shots : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public Guid   RunId      { get; set; }

    private List<ShotSummaryDto>? _shots;
    private string? _error;

    private string ShortRunId => RunId.ToString("N")[..8];

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<List<ShotSummaryDto>>(
            $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots");

        if (!result.IsSuccess) { _error = result.Error.AsAlertText(); return; }
        _shots = result.Value;
    }

    /// <summary>
    /// Visual classification for the four <c>ResultStatus</c>
    /// values the calc seam emits (plus null = idle). Mirrors the
    /// run-status pill palette so a familiar shape applies across
    /// the app.
    /// </summary>
    private static string ResultClass(string? status) => status switch
    {
        "Pending"   => "enki-status-draft",
        "Computing" => "enki-status-inactive",
        "Success"   => "enki-status-active",
        "Failed"    => "enki-status-archived",
        _           => "",
    };
}
