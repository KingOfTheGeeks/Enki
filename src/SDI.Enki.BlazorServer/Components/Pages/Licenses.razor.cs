using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Authorization;
using SDI.Enki.Shared.Licensing;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/licenses")]
[Authorize(Policy = EnkiPolicies.CanManageLicensing)]
public partial class Licenses : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;

    private List<LicenseSummaryDto>? _rows;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<List<LicenseSummaryDto>>("licenses");
        if (!result.IsSuccess) { _error = result.Error.AsAlertText(); return; }
        _rows = result.Value;
    }

    private static bool IsExpiringSoon(LicenseSummaryDto r) =>
        r.Status == "Active" && (r.ExpiresAt - DateTimeOffset.UtcNow).TotalDays < 30;

    private static string StatusClass(string s) => s switch
    {
        "Active"  => "enki-status-active",
        "Revoked" => "enki-status-inactive",
        "Expired" => "enki-status-archived",
        _         => "",
    };
}
