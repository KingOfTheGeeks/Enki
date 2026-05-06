using Microsoft.AspNetCore.Components;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/forbidden")]
public partial class Forbidden : ComponentBase
{
    [Inject] public NavigationManager Nav { get; set; } = default!;

    /// <summary>
    /// Role / permission name shown to the user. Pages that redirect
    /// here can pass it via query param (<c>/forbidden?required=Supervisor</c>).
    /// Blank shows a generic message.
    /// </summary>
    [SupplyParameterFromQuery(Name = "required")]
    public string? RequiredRole { get; set; }

    /// <summary>
    /// Optional resource label (page name, button label) for context.
    /// <c>/forbidden?required=Supervisor&amp;resource=Master+Tools</c>.
    /// </summary>
    [SupplyParameterFromQuery(Name = "resource")]
    public string? Resource { get; set; }
}
