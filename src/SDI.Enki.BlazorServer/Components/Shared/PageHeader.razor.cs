using Microsoft.AspNetCore.Components;

namespace SDI.Enki.BlazorServer.Components.Shared;

public partial class PageHeader : ComponentBase
{
    /// <summary>The page heading. Plain text — markup goes in TitleSuffix.</summary>
    [Parameter, EditorRequired] public string Title { get; set; } = "";

    /// <summary>Optional inline content beside the title (status pill, mono ID, etc.).</summary>
    [Parameter] public RenderFragment? TitleSuffix { get; set; }

    /// <summary>Optional descriptive text under the title — same role as enki-section-subtitle.</summary>
    [Parameter] public RenderFragment? Subtitle { get; set; }

    /// <summary>Action button row, right-aligned on wide viewports, wraps below the title on narrow.</summary>
    [Parameter] public RenderFragment? Actions { get; set; }
}
