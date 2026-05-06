using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Shared.Identity;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/account/settings")]
[Authorize]
public partial class AccountSettings : ComponentBase
{
    [Inject] public IHttpClientFactory      HttpClientFactory { get; set; } = default!;
    [Inject] public UnitPreferenceProvider  UnitPrefs         { get; set; } = default!;

    // ---- preferences card ----
    private UserPreferencesDto? _prefs;
    private string  _unitSystem = "";
    private string? _prefsError;
    private bool    _prefsSaved;
    private bool    _prefsBusy;

    // ---- change-password card ----
    private string _currentPassword = "";
    private string _newPassword     = "";
    private string _confirmPassword = "";
    private string? _passwordError;
    private string? _currentPasswordError;
    private string? _newPasswordError;
    private string? _confirmPasswordError;
    private bool    _passwordSaved;
    private bool    _passwordBusy;

    protected override async Task OnInitializedAsync() => await LoadPreferencesAsync();

    private async Task LoadPreferencesAsync()
    {
        var client = HttpClientFactory.CreateClient("EnkiIdentity");
        var result = await client.GetAsync<UserPreferencesDto>("me/preferences");

        if (!result.IsSuccess) { _prefsError = result.Error.AsAlertText(); return; }
        _prefs      = result.Value;
        _unitSystem = _prefs.PreferredUnitSystem ?? "";
    }

    private async Task SavePreferencesAsync()
    {
        if (_prefsBusy) return;
        _prefsBusy = true; _prefsError = null; _prefsSaved = false;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiIdentity");
            var dto = new UserPreferencesDto(
                PreferredUnitSystem: string.IsNullOrEmpty(_unitSystem) ? null : _unitSystem);

            var result = await client.PutAsync("me/preferences", dto);
            if (!result.IsSuccess) { _prefsError = result.Error.AsAlertText(); return; }

            _prefs       = dto;
            _prefsSaved  = true;

            // Drop the circuit-cached preference so the next page the user
            // navigates to picks up the new unit system without a sign-out
            // / sign-in cycle.
            UnitPrefs.Invalidate();
        }
        finally { _prefsBusy = false; }
    }

    private async Task ChangePasswordAsync()
    {
        if (_passwordBusy) return;

        // Reset all per-field state so a re-submit doesn't show stale errors.
        _passwordError         = null;
        _currentPasswordError  = null;
        _newPasswordError      = null;
        _confirmPasswordError  = null;
        _passwordSaved         = false;

        // Client-side confirm-mismatch check — keeps the obvious typo out
        // of the server round-trip. Server still owns policy enforcement.
        if (_newPassword != _confirmPassword)
        {
            _confirmPasswordError = "New password and confirmation don't match.";
            return;
        }

        _passwordBusy = true;
        try
        {
            var client = HttpClientFactory.CreateClient("EnkiIdentity");
            var result = await client.PostAsync(
                "me/change-password",
                new ChangePasswordDto(_currentPassword, _newPassword));

            if (!result.IsSuccess)
            {
                // Map the server's field-keyed validation errors back onto
                // the UI's per-input slots. Anything not pinned to a known
                // field (Identity emits the rare unknown-code) lands in
                // the card-wide error banner.
                var fields = result.Error.FieldErrors;
                if (fields is not null)
                {
                    if (fields.TryGetValue(nameof(ChangePasswordDto.CurrentPassword), out var curr))
                        _currentPasswordError = string.Join(" ", curr);
                    if (fields.TryGetValue(nameof(ChangePasswordDto.NewPassword), out var newp))
                        _newPasswordError = string.Join(" ", newp);
                    if (fields.TryGetValue(string.Empty, out var generic))
                        _passwordError = string.Join(" ", generic);
                }
                if (_passwordError is null
                    && _currentPasswordError is null
                    && _newPasswordError is null)
                {
                    _passwordError = result.Error.AsAlertText();
                }
                return;
            }

            // Clear the inputs on success so a shoulder-surfer can't read
            // the new password off the screen after the user walks away.
            _currentPassword = "";
            _newPassword     = "";
            _confirmPassword = "";
            _passwordSaved   = true;
        }
        finally { _passwordBusy = false; }
    }
}
