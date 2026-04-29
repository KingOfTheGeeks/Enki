using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SDI.Enki.Identity.Auditing;
using SDI.Enki.Identity.Data;

namespace SDI.Enki.Identity.Pages.Account;

public sealed class LoginModel(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IAuthEventLogger authEvents) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
        ReturnUrl ??= Url.Content("~/");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ReturnUrl ??= Url.Content("~/");

        if (!ModelState.IsValid) return Page();

        var result = await signInManager.PasswordSignInAsync(
            userName:         Input.UserName,
            password:         Input.Password,
            isPersistent:     false,
            lockoutOnFailure: true);

        // Resolve the user (if any) once for the audit row — failed
        // sign-ins against unknown usernames pass through with null.
        var user = await userManager.FindByNameAsync(Input.UserName);

        if (result.Succeeded)
        {
            // Log before redirect — we want the success row written
            // even if the LocalRedirect path throws on a malformed
            // ReturnUrl (LocalRedirect is internally safe but we
            // don't want a future redirect-customisation to swallow
            // the audit).
            await authEvents.LogAsync(
                eventType:  "SignInSucceeded",
                username:   Input.UserName,
                identityId: user?.Id,
                ct:         HttpContext.RequestAborted);

            return LocalRedirect(ReturnUrl);
        }

        // Failure path — record the reason so brute-force / lockout /
        // disabled-account patterns surface in the audit feed.
        var reason = result.IsLockedOut         ? "LockedOut"
                   : result.IsNotAllowed        ? "NotAllowed"
                   : result.RequiresTwoFactor   ? "TwoFactorRequired"
                   : "BadPassword";

        await authEvents.LogAsync(
            eventType:  "SignInFailed",
            username:   Input.UserName,
            identityId: user?.Id,
            detail:     JsonSerializer.Serialize(new { reason }),
            ct:         HttpContext.RequestAborted);

        // SignInManager auto-locks on Nth failure when lockoutOnFailure=true.
        // The current attempt's IsLockedOut tells us *this* attempt was
        // rejected because the account was already locked; if the
        // PasswordSignInAsync call was the one that *triggered* the
        // lock, AccessFailedCount will have just hit MaxFailedAccessAttempts
        // and LockoutEnd will now be in the future. Detect that and
        // log a separate LockoutTriggered row so the lockout is
        // attributable to a specific attempt.
        if (user is not null && !result.IsLockedOut && await userManager.IsLockedOutAsync(user))
        {
            await authEvents.LogAsync(
                eventType:  "LockoutTriggered",
                username:   Input.UserName,
                identityId: user.Id,
                ct:         HttpContext.RequestAborted);
        }

        ErrorMessage = result.IsLockedOut  ? "Account is locked out."
                     : result.IsNotAllowed ? "Account not allowed to sign in."
                     : "Invalid username or password.";
        return Page();
    }

    public sealed class InputModel
    {
        [Required, Display(Name = "Username")]
        public string UserName { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;
    }
}
