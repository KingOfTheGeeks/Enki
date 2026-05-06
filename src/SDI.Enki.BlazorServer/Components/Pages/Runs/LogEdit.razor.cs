using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Logs;
using SDI.Enki.Shared.Runs;

namespace SDI.Enki.BlazorServer.Components.Pages.Runs;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/runs/{RunId:guid}/logs/{LogId:int}/edit")]
[Authorize]
public partial class LogEdit : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }
    [Parameter] public Guid   RunId      { get; set; }
    [Parameter] public int    LogId      { get; set; }

    [SupplyParameterFromForm]
    public EditFormModel? Form { get; set; }

    private string? _loadError;
    private string? _submitError;
    private List<RunCalibrationDto>? _calibrations;

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");

        var calsResult = await client.GetAsync<List<RunCalibrationDto>>(
            $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/calibrations");
        _calibrations = calsResult.IsSuccess ? calsResult.Value : new List<RunCalibrationDto>();

        if (Form is not null) return;

        var result = await client.GetAsync<LogDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/logs/{LogId}");

        if (!result.IsSuccess)
        {
            _loadError = result.Error.Kind == ApiErrorKind.NotFound
                ? $"Log #{LogId} not found."
                : result.Error.AsAlertText();
            return;
        }

        var log = result.Value;
        Form = new EditFormModel
        {
            ShotName            = log.ShotName,
            FileTime            = log.FileTime.LocalDateTime,
            CalibrationIdString = log.CalibrationId?.ToString() ?? "",
            RowVersion          = log.RowVersion,
        };
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _submitError = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new UpdateLogDto(
            ShotName:      Form.ShotName,
            FileTime:      new DateTimeOffset(Form.FileTime, TimeSpan.Zero),
            CalibrationId: ParseCalibrationId(Form.CalibrationIdString),
            RowVersion:    Form.RowVersion);

        var result = await client.PutAsync(
            $"tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/logs/{LogId}", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}/runs/{RunId}/logs");
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

        public string? CalibrationIdString { get; set; }

        public string? RowVersion { get; set; }
    }
}
