using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SDI.Enki.Identity.Auditing;
using SDI.Enki.Identity.Data;

namespace SDI.Enki.Identity.Pages.Account;

public sealed class LogoutModel(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IAuthEventLogger authEvents) : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        // Capture the principal before SignOutAsync clears it — we
        // want the audit row to attribute the sign-out to the user
        // who just signed out, not "anonymous".
        var user = User.Identity?.IsAuthenticated == true
            ? await userManager.GetUserAsync(User)
            : null;

        await signInManager.SignOutAsync();

        await authEvents.LogAsync(
            eventType:  "SignOut",
            username:   user?.UserName ?? "(anonymous)",
            identityId: user?.Id,
            ct:         HttpContext.RequestAborted);

        return Page();
    }
}
