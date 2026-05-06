using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Authorization;
using SDI.Enki.Shared.Tenants;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/tenants/{Code}/edit")]
// Tenant settings (display name, defaults) is master content — Office+
// or admin. Provisioning a new tenant is stricter (Supervisor+) but
// editing the metadata of an existing one is fine for Office.
[Authorize(Policy = EnkiPolicies.CanWriteMasterContent)]
public partial class TenantEdit : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public string Code { get; set; } = "";

    [SupplyParameterFromForm]
    public EditFormModel? Form { get; set; }

    private string?   _loadError;
    private ApiError? _submitError;

    protected override async Task OnInitializedAsync()
    {
        // On GET, populate the form model from the WebApi. On POST, the form
        // already came through [SupplyParameterFromForm] — don't re-fetch.
        if (Form is not null) return;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<TenantDetailDto>($"tenants/{Code}");

        if (!result.IsSuccess)
        {
            _loadError = result.Error.Kind == ApiErrorKind.NotFound
                ? $"Tenant '{Code}' not found."
                : result.Error.AsAlertText();
            return;
        }

        var tenant = result.Value;
        Form = new EditFormModel
        {
            Name         = tenant.Name,
            DisplayName  = tenant.DisplayName,
            ContactEmail = tenant.ContactEmail,
            Notes        = tenant.Notes,
            RowVersion   = tenant.RowVersion,
        };
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _submitError = null;

        var client = HttpClientFactory.CreateClient("EnkiApi");
        var dto = new UpdateTenantDto(
            Name:         Form.Name,
            DisplayName:  Emptied(Form.DisplayName),
            ContactEmail: Emptied(Form.ContactEmail),
            Notes:        Emptied(Form.Notes),
            RowVersion:   Form.RowVersion);

        var result = await client.PutAsync($"tenants/{Code}", dto);
        if (result.IsSuccess)
        {
            Nav.NavigateTo($"/tenants/{Code}");
            return;
        }

        _submitError = result.Error;
    }

    private static string? Emptied(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public sealed class EditFormModel
    {
        [Required(ErrorMessage = "Name is required.")]
        [MaxLength(200, ErrorMessage = "Name must be 200 chars or fewer.")]
        public string Name { get; set; } = "";

        [MaxLength(200, ErrorMessage = "Display name must be 200 chars or fewer.")]
        public string? DisplayName { get; set; }

        [EmailAddress(ErrorMessage = "Not a valid email address.")]
        [MaxLength(256, ErrorMessage = "Email must be 256 chars or fewer.")]
        public string? ContactEmail { get; set; }

        public string? Notes { get; set; }

        public string? RowVersion { get; set; }
    }
}
