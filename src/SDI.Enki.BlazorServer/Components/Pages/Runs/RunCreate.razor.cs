using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Runs;
using SDI.Enki.Shared.Tools;

namespace SDI.Enki.BlazorServer.Components.Pages.Runs;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/runs/new")]
[Authorize]
public partial class RunCreate : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }

    [SupplyParameterFromForm]
    public CreateFormModel? Form { get; set; }

    private string? _submitError;
    private List<ToolSummaryDto>? _tools;

    protected override async Task OnInitializedAsync()
    {
        Form ??= new CreateFormModel();

        // Pull the active-tools list once for the dropdown. Failure to
        // load is non-fatal — the user can still type other fields and
        // submit with no tool assigned.
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<List<ToolSummaryDto>>("tools?status=Active");
        _tools = result.IsSuccess ? result.Value : new List<ToolSummaryDto>();
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _submitError = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new CreateRunDto(
            Name:                Form.Name,
            Description:         Form.Description,
            Type:                Form.Type,
            StartDepth:          Form.StartDepth,
            EndDepth:            Form.EndDepth,
            BTotalNanoTesla:     Form.BTotalNanoTesla,
            DipDegrees:          Form.DipDegrees,
            DeclinationDegrees:  Form.DeclinationDegrees,
            BridleLength:        Form.Type == "Gradient" ? Form.BridleLength     : null,
            CurrentInjection:    Form.Type == "Gradient" ? Form.CurrentInjection : null,
            ToolId:              ParseToolId(Form),
            StartTimestamp:      ToOffset(Form.StartTimestamp),
            EndTimestamp:        ToOffset(Form.EndTimestamp));

        var result = await client.PostAsync<CreateRunDto, RunDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/runs", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/runs/{result.Value.Id}");
            return;
        }

        _submitError = result.Error.AsAlertText();
    }

    private static Guid? ParseToolId(CreateFormModel form)
    {
        if (form.Type == "Passive") return null;
        if (string.IsNullOrWhiteSpace(form.ToolIdString)) return null;
        return Guid.TryParse(form.ToolIdString, out var g) ? g : null;
    }

    private static DateTimeOffset? ToOffset(DateTime? d) =>
        d.HasValue ? new DateTimeOffset(d.Value, TimeSpan.Zero) : null;

    public sealed class CreateFormModel
    {
        [Required(ErrorMessage = "Name is required.")]
        [MaxLength(100, ErrorMessage = "Name must be 100 chars or fewer.")]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "Description is required.")]
        [MaxLength(500, ErrorMessage = "Description must be 500 chars or fewer.")]
        public string Description { get; set; } = "";

        [Required(ErrorMessage = "Run type is required.")]
        public string Type { get; set; } = "Gradient";

        [Required, Range(0d, 100_000d)]
        public double StartDepth { get; set; }

        [Required, Range(0d, 100_000d)]
        public double EndDepth { get; set; }

        // Magnetics — required.
        [Required(ErrorMessage = "BTotal is required.")]
        [Range(20_000d, 80_000d, ErrorMessage = "BTotal must be between 20,000 and 80,000 nT.")]
        public double BTotalNanoTesla { get; set; } = 50_000;   // pre-fill with a sensible default

        [Required(ErrorMessage = "Dip is required.")]
        [Range(-90d, 90d, ErrorMessage = "Dip must be between -90 and 90 degrees.")]
        public double DipDegrees { get; set; } = 60;

        [Required(ErrorMessage = "Declination is required.")]
        [Range(-180d, 180d, ErrorMessage = "Declination must be between -180 and 180 degrees.")]
        public double DeclinationDegrees { get; set; }

        [Range(0d, 100d)]
        public double? BridleLength { get; set; }

        [Range(0d, 1_000d)]
        public double? CurrentInjection { get; set; }

        /// <summary>
        /// Bound to the tool dropdown. Empty string = "(assign later)".
        /// Submit parses to Guid? before posting.
        /// </summary>
        public string? ToolIdString { get; set; }

        public DateTime? StartTimestamp { get; set; }
        public DateTime? EndTimestamp { get; set; }
    }
}
