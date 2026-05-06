using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Components.Layout;
using SDI.Enki.Shared.Authorization;
using SDI.Enki.Shared.Settings;

namespace SDI.Enki.BlazorServer.Components.Pages.Admin;

[Route("/admin/settings")]
[Layout(typeof(AdminLayout))]
[Authorize(Policy = EnkiPolicies.EnkiAdminOnly)]
public partial class AdminSettings : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;

    private List<SystemSettingDto>? _settings;
    private readonly Dictionary<string, string> _buffer = new();
    private string? _listError;
    private string? _actionError;
    private string? _savedKey;
    private string? _resetKey;
    private bool    _busy;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<List<SystemSettingDto>>("admin/settings");

        if (!result.IsSuccess) { _listError = result.Error.AsAlertText(); return; }
        _settings = result.Value;
        foreach (var s in _settings) _buffer[s.Key] = s.Value;
    }

    private async Task SaveAsync(string key)
    {
        if (_busy) return;
        _busy = true; _actionError = null; _savedKey = null; _resetKey = null;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await client.PutAsync(
                $"admin/settings/{Uri.EscapeDataString(key)}",
                new SetSystemSettingDto(_buffer[key]));
            if (!result.IsSuccess)
            {
                _actionError = result.Error.AsAlertText();
                return;
            }
            _savedKey = key;
            await LoadAsync();
        }
        finally { _busy = false; }
    }

    private async Task ResetAsync(string key)
    {
        if (_busy) return;
        _busy = true; _actionError = null; _savedKey = null; _resetKey = null;
        try
        {
            // Server overwrites the row with the canonical default. We
            // re-fetch on success so the textarea snaps to the seeded
            // value without the operator having to click Save afterwards.
            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await client.PostAsync($"admin/settings/{Uri.EscapeDataString(key)}/reset");
            if (!result.IsSuccess)
            {
                _actionError = result.Error.AsAlertText();
                return;
            }
            _resetKey = key;
            await LoadAsync();
        }
        finally { _busy = false; }
    }

    /// <summary>
    /// True for keys whose value is a multi-line list (one item per line).
    /// Drives the input control: multi-line keys get a roomy textarea,
    /// single-value keys get a compact one-line input.
    /// </summary>
    private static bool IsMultiline(string key) => key switch
    {
        "Jobs:RegionSuggestions" => true,
        _                         => false,
    };

    /// <summary>Hand-curated friendly labels per known key. Add as new keys ship.</summary>
    private static string FriendlyName(string key) => key switch
    {
        "Jobs:RegionSuggestions"                 => "Region suggestions",
        "Calibration:Default:GTotal"             => "Default GTotal (mG)",
        "Calibration:Default:BTotal"             => "Default BTotal (nT)",
        "Calibration:Default:DipDegrees"         => "Default Dip (°)",
        "Calibration:Default:DeclinationDegrees" => "Default Declination (°)",
        "Calibration:Default:CoilConstant"       => "Default Coil Constant",
        "Calibration:Default:ActiveBDipDegrees"  => "Default Active-B Dip (°)",
        "Calibration:Default:SampleRateHz"       => "Default Sample Rate (Hz)",
        "Calibration:Default:ManualSign"         => "Default Manual Sign",
        "Calibration:Default:Current"            => "Default Shot Current (A)",
        "Calibration:Default:MagSource"          => "Default Mag Source",
        "Calibration:Default:IncludeDeclination" => "Include declination by default",
        _                                        => key,
    };

    private static string FriendlyHelp(string key) => key switch
    {
        "Jobs:RegionSuggestions" =>
            "One region per line. Drives the suggestion dropdown on the Job " +
            "create / edit pages. Users can still type a free-form value " +
            "that isn't in this list.",

        // Calibration defaults — same ranges the Compute endpoint enforces;
        // saving an out-of-range value here is rejected up front, so an
        // operator can't pre-poison the wizard with values the compute
        // step will reject mid-flow.
        "Calibration:Default:GTotal" =>
            "Reference gravity magnitude in mG. Pre-fills the calibration wizard. " +
            "Must be ≥ 0 (typical: 1000.01).",
        "Calibration:Default:BTotal" =>
            "Reference magnetic field magnitude in nT. Pre-fills the calibration wizard. " +
            "Must be ≥ 0 (typical: 46895).",
        "Calibration:Default:DipDegrees" =>
            "Reference dip angle in degrees. Range: −180 to 180. Typical mid-latitude: 59.867.",
        "Calibration:Default:DeclinationDegrees" =>
            "Reference declination in degrees. Range: −180 to 180. Typical: 12.313.",
        "Calibration:Default:CoilConstant" =>
            "Calibration coil constant. Must be ≥ 0 (typical: 360).",
        "Calibration:Default:ActiveBDipDegrees" =>
            "Active-B reference dip in degrees. Range: −180 to 180. Typical: 89.44.",
        "Calibration:Default:SampleRateHz" =>
            "Sampling rate in Hz. Range: 0.001 to 100000 (typical: 100).",
        "Calibration:Default:ManualSign" =>
            "Manual sign override for active-B polarity. Range: −1 to 1 " +
            "(in practice, ±1; 0 disables the override).",
        "Calibration:Default:Current" =>
            "Default per-shot coil current in amps. Pre-fills the 24 active shot " +
            "currents in the wizard. Must be ≥ 0 (typical: 6.01).",
        "Calibration:Default:MagSource" =>
            "Reference magnetometer mode. Allowed values: 'static' or 'active'.",
        "Calibration:Default:IncludeDeclination" =>
            "Whether to apply declination on the trajectory pass. 'true' or 'false'.",

        _ => "",
    };
}
