using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Shared.Tenants;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/tenants")]
[Authorize]
public partial class Tenants : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;
    [Inject] public IUserCapabilities  Capabilities      { get; set; } = default!;

    private List<TenantSummaryDto>? _tenants;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<List<TenantSummaryDto>>("tenants");

        if (!result.IsSuccess) { _error = result.Error.AsAlertText(); return; }
        _tenants = result.Value;
    }

    private static string StatusClass(string s) => s switch
    {
        "Active"   => "enki-status-active",
        "Inactive" => "enki-status-inactive",
        "Archived" => "enki-status-archived",
        _          => "",
    };
}
