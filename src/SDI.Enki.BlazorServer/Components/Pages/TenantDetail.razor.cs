using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Shared.Tenants;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/tenants/{Code}")]
[Authorize]
public partial class TenantDetail : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;
    [Inject] public IUserCapabilities  Capabilities      { get; set; } = default!;

    [Parameter] public string Code { get; set; } = "";

    [SupplyParameterFromQuery] public string? StatusError { get; set; }

    private TenantDetailDto? _tenant;
    private string? _error;
    private string? _statusError;

    // Cached answer to "may the current user manage members of THIS
    // tenant?" — fetched once in OnInitializedAsync so the markup can
    // gate the Members button via a sync field. Null answer is hidden;
    // we fail closed.
    private bool _canManageMembers;

    protected override async Task OnInitializedAsync()
    {
        _statusError = StatusError;
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<TenantDetailDto>($"tenants/{Code}");

        if (!result.IsSuccess)
        {
            _error = result.Error.Kind == ApiErrorKind.NotFound
                ? $"Tenant '{Code}' not found."
                : result.Error.AsAlertText();
            return;
        }
        _tenant = result.Value;

        // Membership probe. Cached per-circuit on first call so this is
        // cheap on subsequent navigations between tenants in the same
        // session. The Members button stays hidden if the probe fails
        // (defaults to false above) — better than flashing a button
        // that 403s on click.
        _canManageMembers = await Capabilities.CanManageTenantMembersAsync(Code);
    }

    private static string Dashed(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
    private static string DashClass(string? s) => string.IsNullOrWhiteSpace(s) ? "enki-dash" : "";
    private static string StatusClass(string s) => s switch
    {
        "Active"   => "enki-status-active",
        "Inactive" => "enki-status-inactive",
        "Archived" => "enki-status-archived",
        _          => "",
    };
}
