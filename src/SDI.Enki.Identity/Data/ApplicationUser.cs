using Microsoft.AspNetCore.Identity;

namespace SDI.Enki.Identity.Data;

/// <summary>
/// Enki's <see cref="IdentityUser"/> extension. Deliberately thin — profile
/// data lives on the master-DB User entity (linked by <c>IdentityId</c> == this
/// user's <c>Id</c>), so AspNetUsers carries only auth + email + phone.
///
/// <c>UserType</c> is preserved from legacy for forward-compat; today it's
/// just "Team" (SDI employee). When external tenant users come online, expect
/// values like "TenantExternal" or per-tenant discriminators.
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    public string? UserType { get; set; }

    /// <summary>
    /// Cross-tenant SDI admin flag — single source of truth for whether
    /// the user is an Enki admin. <see cref="EnkiUserClaimsPrincipalFactory"/>
    /// derives a <c>role=enki-admin</c> claim from this column at sign-in
    /// (no <c>AspNetUserClaim</c> row), OpenIddict emits the claim in the
    /// access token, and WebApi's <c>CanAccessTenantHandler</c> short-
    /// circuits the TenantUser membership check on it to grant access to
    /// every tenant.
    ///
    /// <para>
    /// Kept here (not on the master-DB <c>User</c>) because token
    /// issuance reads the <see cref="ApplicationUser"/> in
    /// <c>AuthorizationController.Authorize</c> and never touches the
    /// master DB. Writers must (1) flip this column and (2) rotate the
    /// security stamp; the role claim is reissued automatically on the
    /// next sign-in.
    /// </para>
    /// </summary>
    public bool IsEnkiAdmin { get; set; }

    /// <summary>
    /// User-level <see cref="SDI.Enki.Core.Units.UnitSystem"/> preference
    /// — overrides the per-Job default for display. <c>null</c> means
    /// "fall back to the Job's preset" (today's behaviour). Stored as
    /// the SmartEnum's name (Field / Metric / SI) so the column is
    /// human-readable in DB tools without a join.
    ///
    /// Set via <c>/account/settings</c> on the Blazor side; consumed by
    /// the (eventual) display layer that renders Measurement values.
    /// </summary>
    public string? PreferredUnitSystem { get; set; }
}
