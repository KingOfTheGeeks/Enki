using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.Shared.Runs;

namespace SDI.Enki.BlazorServer.Components.Pages.Runs;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}/runs")]
[Authorize]
public partial class Runs : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }

    /// <summary>
    /// Optional <c>?type=Gradient|Rotary|Passive</c> query param. When
    /// set, the grid + the page title filter to that run type. Anything
    /// else (or absent) → unfiltered "all runs" view.
    /// </summary>
    [SupplyParameterFromQuery(Name = "type")]
    public string? TypeFilter { get; set; }

    private List<RunSummaryDto>? _runs;
    private string? _error;

    private string ShortJobId => JobId.ToString("N")[..8];

    /// <summary>
    /// Canonicalised <see cref="TypeFilter"/> — case-insensitive
    /// match against the three valid run types. Anything else
    /// (typo, garbage, missing) collapses to <c>null</c>, which
    /// means "no filter — show everything." The page never throws
    /// on a bad query string; it just renders the all-runs view.
    /// </summary>
    private string? NormalisedType => TypeFilter?.Trim() switch
    {
        var s when string.Equals(s, "Gradient", StringComparison.OrdinalIgnoreCase) => "Gradient",
        var s when string.Equals(s, "Rotary",   StringComparison.OrdinalIgnoreCase) => "Rotary",
        var s when string.Equals(s, "Passive",  StringComparison.OrdinalIgnoreCase) => "Passive",
        _ => null,
    };

    private IReadOnlyList<RunSummaryDto> FilteredRuns
    {
        get
        {
            var source = (IReadOnlyList<RunSummaryDto>?)_runs ?? Array.Empty<RunSummaryDto>();
            return NormalisedType is null
                ? source
                : source.Where(r => r.Type == NormalisedType).ToList();
        }
    }

    private string TitlePrefix => NormalisedType ?? "All";
    private string PageTitlePrefix => NormalisedType is null ? "All" : NormalisedType;

    protected override async Task OnInitializedAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<List<RunSummaryDto>>(
            $"tenants/{TenantCode}/jobs/{JobId}/runs");

        if (!result.IsSuccess) { _error = result.Error.AsAlertText(); return; }
        _runs = result.Value;
    }

    private static string TypeClass(string s) => s switch
    {
        "Gradient" => "enki-status-active",
        "Rotary"   => "enki-status-inactive",
        "Passive"  => "enki-status-draft",
        _          => "",
    };

    private static string StatusClass(string s) => s switch
    {
        "Planned"   => "enki-status-draft",
        "Active"    => "enki-status-active",
        "Suspended" => "enki-status-inactive",
        "Completed" => "enki-status-archived",
        "Cancelled" => "enki-status-archived",
        _           => "",
    };
}
