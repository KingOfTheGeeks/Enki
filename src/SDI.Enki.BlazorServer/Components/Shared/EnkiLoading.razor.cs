using Microsoft.AspNetCore.Components;

namespace SDI.Enki.BlazorServer.Components.Shared;

public partial class EnkiLoading : ComponentBase
{
    /// <summary>
    /// Loading text shown beside the spinner. Pick something specific
    /// on slow-loading pages ("Fetching surveys from API…") so the
    /// user knows what they're waiting on; default "Loading…" is fine
    /// on cheap-fetch pages.
    /// </summary>
    [Parameter] public string Label { get; set; } = "Loading…";
}
