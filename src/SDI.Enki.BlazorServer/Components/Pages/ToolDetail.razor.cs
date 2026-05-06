using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Shared.Calibrations;
using SDI.Enki.Shared.Tools;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/tools/{Serial:int}")]
[Authorize]
public partial class ToolDetail : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;
    [Inject] public IJSRuntime         JS                { get; set; } = default!;
    [Inject] public IUserCapabilities  Capabilities      { get; set; } = default!;

    [Parameter] public int Serial { get; set; }

    [SupplyParameterFromQuery] public string? StatusError { get; set; }

    private ToolDetailDto? _tool;
    private List<CalibrationSummaryDto>? _calibrations;
    private string? _error;
    private string? _statusError;

    // Multi-select state for the calibration grid; drives the Compare button.
    private readonly HashSet<Guid> _selectedCalIds = new();

    // Hardcoded list mirrors ToolDisposition.List on the server. Hardcoding
    // avoids dragging Core into a Blazor page; if a value is added to the
    // SmartEnum, the post-build seed will fail loud and we'll know.
    private static readonly string[] DispositionOptions =
    {
        "Retired", "Lost", "Scrapped", "Sold", "Transferred", "ReturnedToOwner",
    };

    private async Task OpenRetireDialog() =>
        await JS.InvokeVoidAsync("enkiDialog.open", "retire-dialog");

    private async Task CloseRetireDialog() =>
        await JS.InvokeVoidAsync("enkiDialog.close", "retire-dialog");

    private void ToggleCalSelected(Guid id, ChangeEventArgs e)
    {
        if (e.Value is bool b && b)
            _selectedCalIds.Add(id);
        else
            _selectedCalIds.Remove(id);
    }

    private void GoCompare()
    {
        if (_selectedCalIds.Count < 2) return;
        var ids = string.Join(",", _selectedCalIds);
        Nav.NavigateTo($"/tools/{Serial}/calibrations/compare?ids={ids}");
    }

    protected override async Task OnInitializedAsync()
    {
        _statusError = StatusError;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var detail = await client.GetAsync<ToolDetailDto>($"tools/{Serial}");

        if (!detail.IsSuccess)
        {
            _error = detail.Error.Kind == ApiErrorKind.NotFound
                ? $"Tool '{Serial}' not found."
                : detail.Error.AsAlertText();
            return;
        }
        _tool = detail.Value;

        var cals = await client.GetAsync<List<CalibrationSummaryDto>>($"tools/{Serial}/calibrations");
        _calibrations = cals.IsSuccess ? cals.Value : new List<CalibrationSummaryDto>();
    }

    private static string Dashed(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
    private static string DashClass(string? s) => string.IsNullOrWhiteSpace(s) ? "enki-dash" : "";
    private static string StatusClass(string s) => s switch
    {
        "Active"  => "enki-status-active",
        "Retired" => "enki-status-archived",
        "Lost"    => "enki-status-inactive",
        _         => "",
    };
}
