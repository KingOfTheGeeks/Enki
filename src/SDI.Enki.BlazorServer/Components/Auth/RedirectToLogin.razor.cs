using Microsoft.AspNetCore.Components;

namespace SDI.Enki.BlazorServer.Components.Auth;

public partial class RedirectToLogin : ComponentBase
{
    [Inject] public NavigationManager Navigation { get; set; } = default!;

    protected override void OnInitialized()
    {
        var returnUrl = "/" + Navigation.ToBaseRelativePath(Navigation.Uri);
        var target    = $"account/login?returnUrl={Uri.EscapeDataString(returnUrl)}";
        Navigation.NavigateTo(target, forceLoad: true);
    }
}
