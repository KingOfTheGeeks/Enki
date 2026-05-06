using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Jobs;
using SDI.Enki.Shared.Settings;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/tenants/{TenantCode}/jobs/new")]
[Authorize]
public partial class JobCreate : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";

    [SupplyParameterFromForm]
    public CreateForm? Form { get; set; }

    private string?         _error;
    private List<string>    _regions = new();

    protected override void OnInitialized()
    {
        // Same BL0008 dance as TenantCreate — don't initialise a
        // [SupplyParameterFromForm] property in a field initialiser,
        // or the framework can null it during the form post.
        Form ??= new CreateForm
        {
            UnitSystem     = "Field",
            StartTimestamp = DateTime.Today,
            EndTimestamp   = DateTime.Today.AddMonths(1),
        };
    }

    protected override async Task OnInitializedAsync()
    {
        // Region suggestions come from system settings. Failure here
        // shouldn't block the form — just leave the list empty so the
        // user can still type a free-form region.
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<RegionSuggestionsDto>("jobs/region-suggestions");
        if (result.IsSuccess) _regions = result.Value.Regions.ToList();
        // best-effort — soft fall-back to empty list
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _error = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new CreateJobDto(
            Name:           Form.Name,
            Description:    Form.Description,
            UnitSystem:     Form.UnitSystem,
            WellName:       Emptied(Form.WellName),
            Region:         Emptied(Form.Region),
            StartTimestamp: ToOffset(Form.StartTimestamp),
            EndTimestamp:   ToOffset(Form.EndTimestamp));

        var result = await client.PostAsync<CreateJobDto, JobDetailDto>(
            $"tenants/{TenantCode}/jobs", dto);

        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{TenantCode}/jobs/{result.Value.Id}");
            return;
        }

        _error = result.Error.AsAlertText();
    }

    private static string? Emptied(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static DateTimeOffset? ToOffset(DateTime? d) =>
        d.HasValue ? new DateTimeOffset(d.Value.Date, TimeSpan.Zero) : null;

    public sealed class CreateForm
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
    }
}
