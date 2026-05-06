using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Components.Layout;
using SDI.Enki.Shared.Authorization;
using SDI.Enki.Shared.Identity;
using SDI.Enki.Shared.Paging;

namespace SDI.Enki.BlazorServer.Components.Pages.Admin;

[Route("/admin/users")]
[Layout(typeof(AdminLayout))]
// Admin-only because the list dumps EVERY user (Team and Tenant)
// across the system. Office staff who need to manage their own
// tenant's users go through /tenants/{code}/members instead — that
// surface is gated separately by CanManageTenantMembers and naturally
// limits visibility to the bound tenant's members.
[Authorize(Policy = EnkiPolicies.EnkiAdminOnly)]
public partial class AdminUsers : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;

    // Single-page-fetch today (take=500 covers the small SDI roster).
    // Server-side virtual scroll wires straight onto PagedResult — slot
    // a Syncfusion DataAdaptor here when row counts grow.
    private List<AdminUserSummaryDto>? _users;
    private int _total;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiIdentity");
        var result = await client.GetAsync<PagedResult<AdminUserSummaryDto>>(
            "admin/users?skip=0&take=500");

        if (!result.IsSuccess) { _error = result.Error.AsAlertText(); return; }
        _users = result.Value.Items.ToList();
        _total = result.Value.Total;
    }
}
