using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Authorization;
using SDI.Enki.Shared.Tools;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/tools/new")]
[Authorize(Policy = EnkiPolicies.CanManageMasterTools)]
public partial class ToolCreate : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [SupplyParameterFromForm]
    public CreateForm? Form { get; set; }

    private string? _error;
    private IReadOnlyDictionary<string, string[]>? _fieldErrors;

    private static readonly string[] _generations = ["Unknown", "G1", "G2", "G4"];

    protected override void OnInitialized()
    {
        // Per Blazor's BL0008 guidance: don't use a property initializer on
        // a [SupplyParameterFromForm] property (it can get overwritten with
        // null during form post). Populate here instead.
        Form ??= new CreateForm();
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _error = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");

        // Pre-submit uniqueness probe. GET /tools/{serial}: 200 = taken,
        // 404 = free. Saves the user a confusing 409 round-trip.
        var probe = await client.GetAsync<ToolDetailDto>($"tools/{Form.SerialNumber}");
        if (probe.IsSuccess)
        {
            _error = $"Serial {Form.SerialNumber} is already registered. Pick another or edit the existing tool.";
            return;
        }

        var dto = new CreateToolDto(
            SerialNumber:       Form.SerialNumber,
            FirmwareVersion:    Form.FirmwareVersion,
            Configuration:      Form.Configuration,
            Size:               Form.Size,
            MagnetometerCount:  Form.MagnetometerCount,
            AccelerometerCount: Form.AccelerometerCount,
            Generation:         string.IsNullOrWhiteSpace(Form.Generation) ? null : Form.Generation,
            Notes:              Emptied(Form.Notes));

        var result = await client.PostAsync<CreateToolDto, ToolDetailDto>("tools", dto);
        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tools/{Form.SerialNumber}");
            return;
        }

        _error = result.Error.AsAlertText();
        _fieldErrors = result.Error.FieldErrors;
    }

    private static string? Emptied(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public sealed class CreateForm
    {
        [Required(ErrorMessage = "Serial is required.")]
        [Range(1, int.MaxValue, ErrorMessage = "Serial must be a positive integer.")]
        public int SerialNumber { get; set; }

        [Required(ErrorMessage = "Firmware version is required.")]
        [MaxLength(64, ErrorMessage = "Firmware version must be 64 chars or fewer.")]
        public string FirmwareVersion { get; set; } = "";

        public string? Generation { get; set; }

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
    }
}
