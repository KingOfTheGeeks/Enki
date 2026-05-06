using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Runs;
using SDI.Enki.Shared.Tools;

namespace SDI.Enki.BlazorServer.Components.Pages.Runs;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/runs/{RunId:guid}/edit")]
[Authorize]
public partial class RunEdit : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public Guid   RunId      { get; set; }

    [SupplyParameterFromForm]
    public EditFormModel? Form { get; set; }

    private string? _loadError;
    private string? _submitError;
    private List<ToolSummaryDto>? _tools;

    private string ShortRunId => RunId.ToString("N")[..8];

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");

        // Always pull tools so the dropdown is populated even on
        // form-post re-renders. Cheap query (master-side, ~10s of rows).
        var toolsResult = await client.GetAsync<List<ToolSummaryDto>>("tools?status=Active");
        _tools = toolsResult.IsSuccess ? toolsResult.Value : new List<ToolSummaryDto>();

        if (Form is not null) return;

        var result = await client.GetAsync<RunDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}");

        if (!result.IsSuccess)
        {
            _loadError = result.Error.Kind == ApiErrorKind.NotFound
                ? $"Run #{ShortRunId} not found."
                : result.Error.AsAlertText();
            return;
        }

        var run = result.Value;
        Form = new EditFormModel
        {
            Type                = run.Type,
            Name                = run.Name,
            Description         = run.Description,
            StartDepth          = run.StartDepth,
            EndDepth            = run.EndDepth,
            BTotalNanoTesla     = run.BTotalNanoTesla,
            DipDegrees          = run.DipDegrees,
            DeclinationDegrees  = run.DeclinationDegrees,
            BridleLength        = run.BridleLength,
            CurrentInjection    = run.CurrentInjection,
            ToolIdString        = run.ToolId?.ToString() ?? "",
            StartTimestamp      = run.StartTimestamp?.LocalDateTime,
            EndTimestamp        = run.EndTimestamp?.LocalDateTime,
            RowVersion          = run.RowVersion,
        };
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _submitError = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new UpdateRunDto(
            Name:                Form.Name,
            Description:         Form.Description,
            StartDepth:          Form.StartDepth,
            EndDepth:            Form.EndDepth,
            BTotalNanoTesla:     Form.BTotalNanoTesla,
            DipDegrees:          Form.DipDegrees,
            DeclinationDegrees:  Form.DeclinationDegrees,
            BridleLength:        Form.BridleLength,
            CurrentInjection:    Form.CurrentInjection,
            ToolId:              ParseToolId(Form),
            StartTimestamp:      ToOffset(Form.StartTimestamp),
            EndTimestamp:        ToOffset(Form.EndTimestamp),
            RowVersion:          Form.RowVersion);

        var result = await client.PutAsync(
            $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}");
            return;
        }

        _submitError = result.Error.AsAlertText();
    }

    private static Guid? ParseToolId(EditFormModel form)
    {
        if (form.Type == "Passive") return null;
        if (string.IsNullOrWhiteSpace(form.ToolIdString)) return null;
        return Guid.TryParse(form.ToolIdString, out var g) ? g : null;
    }

    private static DateTimeOffset? ToOffset(DateTime? d) =>
        d.HasValue ? new DateTimeOffset(d.Value, TimeSpan.Zero) : null;

    public sealed class EditFormModel
    {
        // Type is read-only on edit; preserved through the round-trip
        // for the Gradient-only field gating but never changed.
        public string Type { get; set; } = "";

        [Required(ErrorMessage = "Name is required.")]
        [MaxLength(100, ErrorMessage = "Name must be 100 chars or fewer.")]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "Description is required.")]
        [MaxLength(500, ErrorMessage = "Description must be 500 chars or fewer.")]
        public string Description { get; set; } = "";

        [Required, Range(0d, 100_000d)]
        public double StartDepth { get; set; }

        [Required, Range(0d, 100_000d)]
        public double EndDepth { get; set; }

        [Required(ErrorMessage = "BTotal is required.")]
        [Range(20_000d, 80_000d, ErrorMessage = "BTotal must be between 20,000 and 80,000 nT.")]
        public double BTotalNanoTesla { get; set; }

        [Required(ErrorMessage = "Dip is required.")]
        [Range(-90d, 90d, ErrorMessage = "Dip must be between -90 and 90 degrees.")]
        public double DipDegrees { get; set; }

        [Required(ErrorMessage = "Declination is required.")]
        [Range(-180d, 180d, ErrorMessage = "Declination must be between -180 and 180 degrees.")]
        public double DeclinationDegrees { get; set; }

        [Range(0d, 100d)]
        public double? BridleLength { get; set; }

        [Range(0d, 1_000d)]
        public double? CurrentInjection { get; set; }

        public string? ToolIdString { get; set; }

        public DateTime? StartTimestamp { get; set; }
        public DateTime? EndTimestamp { get; set; }

        public string? RowVersion { get; set; }
    }
}
