using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Authorization;
using SDI.Enki.Shared.Tenants;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/tenants/new")]
[Authorize(Policy = EnkiPolicies.CanProvisionTenants)]
public partial class TenantCreate : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    /// <summary>
    /// Bound from the posted form. <c>[SupplyParameterFromForm]</c> is the
    /// Blazor-Web-App way to hydrate a property directly from the form post
    /// without manually reading the request. Works with static SSR, which is
    /// important here because we need the server-side HttpContext alive so
    /// the BearerTokenHandler can attach the access token to the /tenants
    /// POST.
    /// </summary>
    [SupplyParameterFromForm]
    public CreateForm? Form { get; set; }

    private string? _error;

    protected override void OnInitialized()
    {
        // Per Blazor's BL0008 guidance: don't use a property initializer on
        // a [SupplyParameterFromForm] property (it can get overwritten with
        // null during form post). Populate here instead so GETs get a fresh
        // instance and POSTs keep whatever the form sent.
        Form ??= new CreateForm();
    }

    private async Task Submit()
    {
        if (Form is null) return;
        _error = null;
        var code = Form.Code.ToUpperInvariant();

        var client = HttpClientFactory.CreateClient("EnkiApi");

        // Pre-submit uniqueness probe — catches "code already taken"
        // before kicking off the (expensive, side-effectful) real
        // provisioning call. GET /tenants/{code}: 200 = taken, 404 = free.
        var probe = await client.GetAsync<TenantDetailDto>($"tenants/{code}");
        if (probe.IsSuccess)
        {
            _error = $"Tenant code '{code}' is already in use. Pick another.";
            return;
        }

        var dto = new ProvisionTenantDto(
            Code:         code,
            Name:         Form.Name,
            DisplayName:  Emptied(Form.DisplayName),
            ContactEmail: Emptied(Form.ContactEmail),
            Notes:        Emptied(Form.Notes));

        var result = await client.PostAsync("tenants", dto);
        if (result.IsSuccess)
        {
            // Go straight to Jobs — the next thing the user does
            // on a fresh tenant is start adding work. Tenant
            // identity / edit reachable via sidebar Overview.
            Nav.NavigateTo($"/tenants/{dto.Code}/jobs");
            return;
        }

        _error = result.Error.AsAlertText();
    }

    // Treat all-whitespace / empty strings as null so the DB doesn't get
    // rows with Region="", ContactEmail="", etc.
    private static string? Emptied(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public sealed class CreateForm
    {
        // Client regex is case-insensitive — the input field auto-uppercases
        // as the user types and Submit() normalises with ToUpperInvariant()
        // before sending. The server-side regex on ProvisionTenantDto stays
        // strict-uppercase; if anything ever bypasses the client and sends
        // lowercase, it fails fast with a clear 400.
        [Required(ErrorMessage = "Code is required.")]
        [RegularExpression(@"^[A-Za-z][A-Za-z0-9_]{0,23}$",
            ErrorMessage = "Code must be 1–24 chars, letters / digits / underscore, starting with a letter.")]
        public string Code { get; set; } = "";

        [Required(ErrorMessage = "Name is required.")]
        [MaxLength(200, ErrorMessage = "Name must be 200 chars or fewer.")]
        public string Name { get; set; } = "";

        [MaxLength(200, ErrorMessage = "Display name must be 200 chars or fewer.")]
        public string? DisplayName { get; set; }

        [EmailAddress(ErrorMessage = "Not a valid email address.")]
        [MaxLength(256, ErrorMessage = "Email must be 256 chars or fewer.")]
        public string? ContactEmail { get; set; }

        public string? Notes { get; set; }
    }
}
