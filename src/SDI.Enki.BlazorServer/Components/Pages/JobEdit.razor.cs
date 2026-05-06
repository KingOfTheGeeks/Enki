using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Settings;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/edit")]
[Authorize]
public partial class JobEdit : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }

    [SupplyParameterFromForm]
    public EditFormModel? Form { get; set; }

    private string?      _loadError;
    private ApiError?    _submitError;
    private List<string> _regions = new();

    protected override async Task OnInitializedAsync()
    {
        // Region suggestions — same pattern as JobCreate. Best-effort;
        // failure leaves the suggestion list empty rather than blocking.
        var settings = HttpClientFactory.CreateClient("EnkiApi");
        var regionsResult = await settings.GetAsync<RegionSuggestionsDto>("jobs/region-suggestions");
        if (regionsResult.IsSuccess) _regions = regionsResult.Value.Regions.ToList();

        // GET populates the form from the WebApi; POST already has
        // Form hydrated by [SupplyParameterFromForm].
        if (Form is not null) return;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var jobResult = await client.GetAsync<JobDetailDto>(
            $"tenants/{TenantCode}/jobs/{JobId}");

        if (!jobResult.IsSuccess)
        {
            _loadError = jobResult.Error.Kind == ApiErrorKind.NotFound
                ? $"Job #{JobId} not found."
                : jobResult.Error.AsAlertText();
            return;
        }

        var job = jobResult.Value;
        Form = new EditFormModel
        {
            Name           = job.Name,
            Description    = job.Description,
            UnitSystem     = job.UnitSystem,
            WellName       = job.WellName,
            Region         = job.Region,
            // Calendar dates — read the offset's underlying calendar
            // date directly via .Date, not .LocalDateTime.Date. The
            // localising path drifts the day every save: with the
            // server in UTC-6, a stored 2026-05-03T00:00:00Z read as
            // .LocalDateTime.Date gives 2026-05-02, the next save
            // coerces back to midnight UTC, the next load gives
            // 2026-05-01, and so on. Treating these as calendar dates
            // (date-as-stored, no tz projection) keeps the round-trip
            // stable for every viewer regardless of their timezone.
            StartTimestamp = job.StartTimestamp.Date,
            EndTimestamp   = job.EndTimestamp.Date,
            RowVersion     = job.RowVersion,
        };
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _submitError = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new UpdateJobDto(
            Name:           Form.Name,
            Description:    Form.Description,
            UnitSystem:     Form.UnitSystem,
            WellName:       Emptied(Form.WellName),
            Region:         Emptied(Form.Region),
            StartTimestamp: ToOffset(Form.StartTimestamp),
            EndTimestamp:   ToOffset(Form.EndTimestamp),
            RowVersion:     Form.RowVersion);

        var result = await client.PutAsync(
            $"tenants/{TenantCode}/jobs/{JobId}", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{JobId}");
            return;
        }

        _submitError = result.Error;
    }

    private static string? Emptied(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static DateTimeOffset? ToOffset(DateTime? d) =>
        d.HasValue ? new DateTimeOffset(d.Value.Date, TimeSpan.Zero) : null;

    public sealed class EditFormModel
    {
        [Required(ErrorMessage = "Name is required.")]
        [MaxLength(50, ErrorMessage = "Name must be 50 chars or fewer.")]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "Description is required.")]
        [MaxLength(200, ErrorMessage = "Description must be 200 chars or fewer.")]
        public string Description { get; set; } = "";

        [Required(ErrorMessage = "Unit system is required.")]
        public string UnitSystem { get; set; } = "Field";

        [MaxLength(100, ErrorMessage = "Well name must be 100 chars or fewer.")]
        public string? WellName { get; set; }

        [MaxLength(64, ErrorMessage = "Region must be 64 chars or fewer.")]
        public string? Region { get; set; }

        public DateTime? StartTimestamp { get; set; }
        public DateTime? EndTimestamp { get; set; }

        public string? RowVersion { get; set; }
    }
}
