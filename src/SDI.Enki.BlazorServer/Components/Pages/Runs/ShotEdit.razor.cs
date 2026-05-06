using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Runs;
using SDI.Enki.Shared.Shots;

namespace SDI.Enki.BlazorServer.Components.Pages.Runs;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/runs/{RunId:guid}/shots/{ShotId:int}/edit")]
[Authorize]
public partial class ShotEdit : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public Guid   RunId      { get; set; }
    [Parameter] public int    ShotId     { get; set; }

    [SupplyParameterFromForm]
    public EditFormModel? Form { get; set; }

    private string? _loadError;
    private string? _submitError;
    private List<RunCalibrationDto>? _calibrations;

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");

        // Pull the run's available calibration snapshots so the
        // dropdown stays populated across form-post re-renders.
        var calsResult = await client.GetAsync<List<RunCalibrationDto>>(
            $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/calibrations");
        _calibrations = calsResult.IsSuccess ? calsResult.Value : new List<RunCalibrationDto>();

        if (Form is not null) return;

        var result = await client.GetAsync<ShotDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{ShotId}");

        if (!result.IsSuccess)
        {
            _loadError = result.Error.Kind == ApiErrorKind.NotFound
                ? $"Shot #{ShotId} not found."
                : result.Error.AsAlertText();
            return;
        }

        var shot = result.Value;
        Form = new EditFormModel
        {
            ShotName            = shot.ShotName,
            FileTime            = shot.FileTime.LocalDateTime,
            CalibrationIdString = shot.CalibrationId?.ToString() ?? "",
            RowVersion          = shot.RowVersion,
        };
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _submitError = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new UpdateShotDto(
            ShotName:      Form.ShotName,
            FileTime:      new DateTimeOffset(Form.FileTime, TimeSpan.Zero),
            CalibrationId: ParseCalibrationId(Form.CalibrationIdString),
            RowVersion:    Form.RowVersion);

        var result = await client.PutAsync(
            $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{ShotId}", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/shots/{ShotId}");
            return;
        }

        _submitError = result.Error.AsAlertText();
    }

    private static int? ParseCalibrationId(string? raw) =>
        int.TryParse(raw, out var v) ? v : null;

    public sealed class EditFormModel
    {
        [Required(ErrorMessage = "Shot name is required.")]
        [MaxLength(200, ErrorMessage = "Shot name must be 200 chars or fewer.")]
        public string ShotName { get; set; } = "";

        [Required(ErrorMessage = "File time is required.")]
        public DateTime FileTime { get; set; }

        /// <summary>
        /// Bound to the calibration dropdown. Empty string = "(none)";
        /// otherwise the int id of a tenant Calibration row exposed by
        /// <c>GET /runs/{runId}/calibrations</c>.
        /// </summary>
        public string? CalibrationIdString { get; set; }

        public string? RowVersion { get; set; }
    }
}
