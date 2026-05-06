using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Logs;

namespace SDI.Enki.BlazorServer.Components.Pages.Runs;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/runs/{RunId:guid}/logs")]
[Authorize]
public partial class Logs : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public Guid   RunId      { get; set; }

    private List<LogSummaryDto>? _logs;
    private string? _error;

    private string ShortRunId => RunId.ToString("N")[..8];

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<List<LogSummaryDto>>(
            $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/logs");

        if (!result.IsSuccess) { _error = result.Error.AsAlertText(); return; }
        _logs = result.Value;
    }
}
