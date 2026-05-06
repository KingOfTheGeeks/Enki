using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Shared.Tools;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/tools")]
[Authorize]
public partial class Tools : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public IUserCapabilities  Capabilities      { get; set; } = default!;

    private List<ToolSummaryDto>? _tools;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<List<ToolSummaryDto>>("tools");

        if (!result.IsSuccess) { _error = result.Error.AsAlertText(); return; }
        _tools = result.Value;
    }

    private static bool NeedsCalibration(ToolSummaryDto t) =>
        t.Status == "Active" &&
        (t.LatestCalibrationDate is null ||
         (DateTimeOffset.UtcNow - t.LatestCalibrationDate.Value).TotalDays > 365);

    private static string StatusClass(string s) => s switch
    {
        "Active"  => "enki-status-active",
        "Retired" => "enki-status-archived",
        "Lost"    => "enki-status-inactive",
        _         => "",
    };
}
