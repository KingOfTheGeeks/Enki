using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SDI.Enki.Identity.Data;

namespace SDI.Enki.Identity.Pages.Account;

public sealed class LoginModel(SignInManager<ApplicationUser> signInManager) : PageModel
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

        if (result.Succeeded)
            return LocalRedirect(ReturnUrl);

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
