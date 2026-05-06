using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Authorization;
using SDI.Enki.Shared.Tools;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/tools/{Serial:int}/edit")]
[Authorize(Policy = EnkiPolicies.CanManageMasterTools)]
public partial class ToolEdit : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public int Serial { get; set; }

    [SupplyParameterFromForm]
    public EditFormModel? Form { get; set; }

    private string? _loadError;
    private string? _submitError;
    private IReadOnlyDictionary<string, string[]>? _fieldErrors;

    private static readonly string[] _generations = ["Unknown", "G1", "G2", "G4"];

    protected override async Task OnInitializedAsync()
    {
        if (Form is not null) return;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<ToolDetailDto>($"tools/{Serial}");

        if (!result.IsSuccess)
        {
            _loadError = result.Error.Kind == ApiErrorKind.NotFound
                ? $"Tool '{Serial}' not found."
                : result.Error.AsAlertText();
            return;
        }

        var tool = result.Value;
        Form = new EditFormModel
        {
            SerialNumber       = tool.SerialNumber,
            FirmwareVersion    = tool.FirmwareVersion,
            Generation         = tool.Generation,
            Configuration      = tool.Configuration,
            Size               = tool.Size,
            MagnetometerCount  = tool.MagnetometerCount,
            AccelerometerCount = tool.AccelerometerCount,
            Notes              = tool.Notes,
            RowVersion         = tool.RowVersion,
        };
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _submitError = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new UpdateToolDto(
            SerialNumber:       Form.SerialNumber,
            FirmwareVersion:    Form.FirmwareVersion,
            Generation:         Form.Generation,
            Configuration:      Form.Configuration,
            Size:               Form.Size,
            MagnetometerCount:  Form.MagnetometerCount,
            AccelerometerCount: Form.AccelerometerCount,
            Notes:              Emptied(Form.Notes),
            RowVersion:         Form.RowVersion);

        // Route by current Serial (the URL the user is on); the body carries
        // the new SerialNumber, so a rename succeeds and we redirect to the
        // new URL once SaveChanges lands.
        var result = await client.PutAsync($"tools/{Serial}", dto);
        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tools/{Form.SerialNumber}");
            return;
        }

        _submitError = result.Error.AsAlertText();
        _fieldErrors = result.Error.FieldErrors;
    }

    private static string? Emptied(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public sealed class EditFormModel
    {
        [Required(ErrorMessage = "Serial is required.")]
        [Range(1, int.MaxValue, ErrorMessage = "Serial must be a positive integer.")]
        public int SerialNumber { get; set; }

        [Required(ErrorMessage = "Firmware version is required.")]
        [MaxLength(64, ErrorMessage = "Firmware version must be 64 chars or fewer.")]
        public string FirmwareVersion { get; set; } = "";

        [Required(ErrorMessage = "Generation is required.")]
        public string Generation { get; set; } = "Unknown";

        [Range(0, int.MaxValue, ErrorMessage = "Configuration must be non-negative.")]
        public int Configuration { get; set; }

        [Range(0, int.MaxValue, ErrorMessage = "Size must be non-negative.")]
        public int Size { get; set; }

        [Range(0, 16, ErrorMessage = "Magnetometer count must be 0–16.")]
        public int MagnetometerCount { get; set; }

        [Range(0, 16, ErrorMessage = "Accelerometer count must be 0–16.")]
        public int AccelerometerCount { get; set; }

        [MaxLength(1000, ErrorMessage = "Notes must be 1000 chars or fewer.")]
        public string? Notes { get; set; }

        public string? RowVersion { get; set; }
    }
}
