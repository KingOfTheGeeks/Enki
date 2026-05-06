using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Logs;
using SDI.Enki.Shared.Runs;

namespace SDI.Enki.BlazorServer.Components.Pages.Runs;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/runs/{RunId:guid}/logs/new")]
[Authorize]
public partial class LogCreate : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public Guid   RunId      { get; set; }

    [SupplyParameterFromForm]
    public CreateFormModel? Form { get; set; }

    private string? _submitError;
    private List<RunCalibrationDto>? _calibrations;

    private string ShortRunId => RunId.ToString("N")[..8];

    protected override async Task OnInitializedAsync()
    {
        Form ??= new CreateFormModel { FileTime = DateTime.Now };

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var calsResult = await client.GetAsync<List<RunCalibrationDto>>(
            $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/calibrations");
        _calibrations = calsResult.IsSuccess ? calsResult.Value : new List<RunCalibrationDto>();
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _submitError = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new CreateLogDto(
            ShotName:      Form.ShotName,
            FileTime:      new DateTimeOffset(Form.FileTime, TimeSpan.Zero),
            CalibrationId: ParseCalibrationId(Form.CalibrationIdString));

        var result = await client.PostAsync<CreateLogDto, LogDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/logs", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/logs");
            return;
        }

        _submitError = result.Error.AsAlertText();
    }

    private static int? ParseCalibrationId(string? raw) =>
        int.TryParse(raw, out var v) ? v : null;

    public sealed class CreateFormModel
    {
        [Required(ErrorMessage = "Shot name is required.")]
        [MaxLength(200, ErrorMessage = "Shot name must be 200 chars or fewer.")]
        public string ShotName { get; set; } = "";

        [Required(ErrorMessage = "File time is required.")]
        public DateTime FileTime { get; set; } = DateTime.Now;

        /// <summary>
        /// Bound to the calibration dropdown. Empty string = "default
        /// (run's snapshot)" — server fills it in.
        /// </summary>
        public string? CalibrationIdString { get; set; }
    }
}
