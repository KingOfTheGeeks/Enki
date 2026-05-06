using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Authorization;
using SDI.Enki.Shared.Licensing;
using SDI.Enki.Shared.Tools;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/licenses/new")]
[Authorize(Policy = EnkiPolicies.CanManageLicensing)]
public partial class LicenseCreate : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    private List<ToolSummaryDto>? _tools;
    private List<ToolSummaryDto> _eligibleTools = new();
    private string? _error;
    private bool _busy;

    private string _licensee = "";
    private string _licenseKey = "";
    private DateTime _expiresAt = DateTime.UtcNow.Date.AddYears(1);
    private HashSet<Guid> _selectedToolIds = new();

    // Display order matches Nabu's Licensing.razor: Warrior + variants
    // first, then per-app flags. WarriorLogging right next to Warrior so
    // the dependency relationship is obvious in the UI.
    private static readonly string[] _featureOrder =
    [
        "AllowWarrior",
        "AllowWarriorLogging",
        "AllowNorthSea",
        "AllowSerial",
        "AllowGradient",
        "AllowRotary",
        "AllowPassive",
        "AllowCalibrate",
        "AllowSurvey",
        "AllowResults",
        "AllowGyro",
    ];

    private Dictionary<string, bool> _features = new()
    {
        ["AllowWarrior"]        = true,
        ["AllowWarriorLogging"] = false,
        ["AllowNorthSea"]       = false,
        ["AllowSerial"]         = false,
        ["AllowGradient"]       = true,
        ["AllowRotary"]         = false,
        ["AllowPassive"]        = false,
        ["AllowCalibrate"]      = false,
        ["AllowSurvey"]         = true,
        ["AllowResults"]        = true,
        ["AllowGyro"]           = false,
    };

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<List<ToolSummaryDto>>("tools");
        if (!result.IsSuccess) { _error = result.Error.AsAlertText(); return; }
        _tools = result.Value;

        // Filter: only tools with a real current calibration on file.
        // ToolSummaryDto.LatestCalibrationDate is null when there's none;
        // CalibrationCount > 0 catches the case where a cal exists but date
        // can't be projected. Order by serial for predictability.
        _eligibleTools = _tools!
            .Where(t => t.CalibrationCount > 0 && t.LatestCalibrationDate is not null)
            .OrderBy(t => t.SerialNumber)
            .ToList();
    }

    private void GenerateKey()
    {
        // GUID "D" format = 8-4-4-4-12 hex with hyphens, lowercase. Marduk's
        // HeimdallEnvelopeDecryptor's NormalizeKey accepts any reasonable
        // GUID variant (D/N/B/P + casing), but pinning to D matches what
        // the generator uses for PBKDF2.
        _licenseKey = Guid.NewGuid().ToString("D");
    }

    private void ToggleTool(Guid id, ChangeEventArgs e)
    {
        if (e.Value is bool b && b) _selectedToolIds.Add(id);
        else _selectedToolIds.Remove(id);
    }

    private void SelectAllTools()
    {
        foreach (var t in _eligibleTools)
            _selectedToolIds.Add(t.Id);
    }

    private void DeselectAllTools() => _selectedToolIds.Clear();

    private void SetFeature(string key, ChangeEventArgs e)
    {
        if (e.Value is not bool b) return;
        _features[key] = b;

        // Domain rule: WarriorLogging requires Warrior. Both directions:
        //  - Turn ON WarriorLogging  → auto-enable Warrior
        //  - Turn OFF Warrior        → auto-disable WarriorLogging
        if (key == "AllowWarriorLogging" && b)
            _features["AllowWarrior"] = true;
        if (key == "AllowWarrior" && !b)
            _features["AllowWarriorLogging"] = false;
    }

    private bool CanGenerate()
    {
        if (_busy) return false;
        if (string.IsNullOrWhiteSpace(_licensee)) return false;
        if (!Guid.TryParse(_licenseKey, out var g) || g == Guid.Empty) return false;
        if (_selectedToolIds.Count == 0) return false;
        return true;
    }

    private async Task GenerateAsync()
    {
        if (!CanGenerate()) return;
        if (!Guid.TryParse(_licenseKey, out var key)) return;

        _busy = true;
        _error = null;

        try
        {
            var dto = new CreateLicenseDto(
                Licensee:                       _licensee,
                LicenseKey:                     key,
                ExpiresAt:                      DateTime.SpecifyKind(_expiresAt.Date, DateTimeKind.Utc),
                ToolIds:                        _selectedToolIds.ToList(),
                CalibrationOverridesByToolId:   null,   // current cal per tool
                Features:                       new LicenseFeaturesDto(
                    AllowWarrior:        _features["AllowWarrior"],
                    AllowNorthSea:       _features["AllowNorthSea"],
                    AllowSerial:         _features["AllowSerial"],
                    AllowRotary:         _features["AllowRotary"],
                    AllowGradient:       _features["AllowGradient"],
                    AllowPassive:        _features["AllowPassive"],
                    AllowWarriorLogging: _features["AllowWarriorLogging"],
                    AllowCalibrate:      _features["AllowCalibrate"],
                    AllowSurvey:         _features["AllowSurvey"],
                    AllowResults:        _features["AllowResults"],
                    AllowGyro:           _features["AllowGyro"]));

            var client = HttpClientFactory.CreateClient("EnkiApi");
            var result = await client.PostAsync<CreateLicenseDto, LicenseDetailDto>("licenses", dto);

            if (!result.IsSuccess)
            {
                _error = result.Error.AsAlertText();
                return;
            }

            // Detail page shows both download buttons + the snapshot view.
            Nav.NavigateTo($"/licenses/{result.Value!.Id}");
        }
        catch (Exception ex)
        {
            _error = $"Generation failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
        }
    }
}
