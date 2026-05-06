using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Shared.Wells;

namespace SDI.Enki.BlazorServer.Components.Pages.Wells;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/wells")]
[Authorize]
public partial class Wells : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;
    [Inject] public IUserCapabilities  Capabilities      { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }

    private List<WellSummaryDto>? _wells;
    private string? _error;
    private bool _canWrite;

    private string ShortJobId => JobId.ToString("N")[..8];

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<List<WellSummaryDto>>(
            $"tenants/{TenantCode}/jobs/{JobId}/wells");

        if (!result.IsSuccess) { _error = result.Error.AsAlertText(); return; }
        _wells = result.Value;

        _canWrite = await Capabilities.CanWriteTenantContentAsync(TenantCode);
    }

    private static string TypeClass(string s) => s switch
    {
        "Target"    => "enki-status-active",
        "Intercept" => "enki-status-inactive",
        "Offset"    => "enki-status-archived",
        _           => "",
    };
}
