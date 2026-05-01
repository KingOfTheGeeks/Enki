namespace SDI.Enki.Identity.Configuration;

/// <summary>
/// Token + cookie lifetime knobs for the Identity host. Bound from the
/// <c>SessionLifetime</c> config section.
///
/// <para>
/// Three values, three jobs:
/// </para>
/// <list type="bullet">
///   <item>
///     <see cref="AccessTokenLifetimeMinutes"/> — how long an issued JWT
///     access token is valid. Stays short across the board because the
///     access token isn't server-revocable; sliding refresh handles the
///     "stay logged in" UX.
///   </item>
///   <item>
///     <see cref="RefreshTokenLifetimeMinutes"/> — sliding refresh-token
///     lifetime for users who don't have a per-user override. Each
///     successful refresh resets the window to "now + this value".
///   </item>
///   <item>
///     <see cref="MaxRefreshTokenLifetimeMinutes"/> — server-side ceiling.
///     Both the admin endpoint that writes <c>ApplicationUser.SessionLifetimeMinutes</c>
///     and the OpenIddict per-request handler clamp to this. Raise
///     when MFA is mandatory on long-lived sessions; until then the
///     security floor depends on this being a sane number.
///   </item>
/// </list>
/// </summary>
public sealed class SessionLifetimeOptions
{
    public const string SectionName = "SessionLifetime";

    /// <summary>
    /// Lifetime of issued access tokens. Default 60 minutes (matches the
    /// pre-#30 behaviour and what every existing client expects).
    /// </summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// Default sliding refresh-token lifetime. Default 14400 minutes
    /// (10 days) — long enough that returning users don't get bounced
    /// to login most working weeks, short enough that a forgotten device
    /// stops minting tokens within a fortnight.
    /// </summary>
    public int RefreshTokenLifetimeMinutes { get; set; } = 14400;

    /// <summary>
    /// Hard ceiling for any per-user override. Default 525600 minutes
    /// (1 year). The admin UI clamps to this; the OpenIddict per-request
    /// handler also clamps so a stale row written before the ceiling
    /// dropped can't escape the new policy.
    /// </summary>
    public int MaxRefreshTokenLifetimeMinutes { get; set; } = 525600;
}
