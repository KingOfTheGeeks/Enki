using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Shared.Jobs;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/tenants/{TenantCode}/jobs")]
[Authorize]
public partial class Jobs : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;
    [Inject] public IUserCapabilities  Capabilities      { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";

    private List<JobSummaryDto>? _jobs;
    private string? _error;
    private bool _canWrite;

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<List<JobSummaryDto>>(
            $"tenants/{TenantCode}/jobs");

        if (!result.IsSuccess) { _error = result.Error.AsAlertText(); return; }
        _jobs = result.Value;

        // Tenant-scoped write capability — used to gate the "+ New job"
        // button. Membership probe is cached per-circuit; cheap on
        // subsequent navigations.
        _canWrite = await Capabilities.CanWriteTenantContentAsync(TenantCode);
    }

    private static string StatusClass(string s) => s switch
    {
        "Draft"     => "enki-status-inactive",
        "Active"    => "enki-status-active",
        "Completed" => "enki-status-active",
        "Archived"  => "enki-status-archived",
        _           => "",
    };
}
