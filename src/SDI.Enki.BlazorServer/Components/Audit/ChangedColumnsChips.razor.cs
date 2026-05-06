using Microsoft.AspNetCore.Components;

namespace SDI.Enki.BlazorServer.Components.Audit;

public partial class ChangedColumnsChips : ComponentBase
{
    /// <summary>Pipe-delimited list of property names that changed.</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>The audit row's Action — drives Created/Deleted fallback rendering.</summary>
    [Parameter] public string Action { get; set; } = "";
}
