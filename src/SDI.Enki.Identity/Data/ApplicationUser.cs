using Microsoft.AspNetCore.Identity;
using SharedUserType = SDI.Enki.Shared.Identity.UserType;

namespace SDI.Enki.Identity.Data;

/// <summary>
/// Enki's <see cref="IdentityUser"/> extension. Deliberately thin —
/// profile data lives on the master-DB User entity (linked by
/// <c>IdentityId</c> == this user's <c>Id</c>), so AspNetUsers carries
/// only auth + email + phone.
///
/// <para>
/// <see cref="UserType"/> is the SDI-vs-tenant-external discriminator;
/// today every account is <see cref="SharedUserType.Team"/>. The
/// SmartEnum replaces the previous free-string column so a typo in
/// seed data or a future admin endpoint can't silently store a value
/// no reader understands. Persisted via a value converter on
/// <see cref="ApplicationDbContext"/>.
/// </para>
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    public SharedUserType? UserType { get; set; }

    /// <summary>
    /// Cross-tenant SDI admin flag — single source of truth for whether
    /// the user is an Enki admin. <see cref="EnkiUserClaimsPrincipalFactory"/>
    /// derives a <c>role=enki-admin</c> claim from this column at sign-in
    /// (no <c>AspNetUserClaim</c> row), OpenIddict emits the claim in the
    /// access token, and WebApi's <c>TeamAuthHandler</c> short-circuits
    /// every membership, subtype, and capability check on it to grant
    /// access across the whole system.
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

    /// <summary>
    /// Per-user override for the OAuth refresh-token lifetime, in minutes.
    /// <c>null</c> means "use the global default" (<c>SessionLifetimeOptions.RefreshTokenLifetimeMinutes</c>) —
    /// the column only widens the sliding-refresh window for opt-in
    /// trusted users (admins, on-call ops) so they aren't bounced to the
    /// login page mid-session.
    ///
    /// <para>
    /// Clamped against <c>SessionLifetimeOptions.MaxRefreshTokenLifetimeMinutes</c>
    /// at issuance time — even if a controller writes a value above the
    /// configured ceiling, the OpenIddict pipeline caps it. The column
    /// itself isn't enforced at the DB layer because the ceiling is a
    /// runtime config knob (raisable when MFA lands), not a schema one.
    /// </para>
    ///
    /// <para>
    /// Setters MUST also stamp <see cref="SessionLifetimeUpdatedAt"/> +
    /// <see cref="SessionLifetimeUpdatedBy"/> and call
    /// <c>UserManager.UpdateSecurityStampAsync</c> so any in-flight
    /// refresh tokens issued under the prior policy are invalidated on
    /// the next exchange — otherwise the old window stays in effect
    /// until that refresh token expires naturally.
    /// </para>
    /// </summary>
    public int? SessionLifetimeMinutes { get; set; }

    /// <summary>
    /// When <see cref="SessionLifetimeMinutes"/> was last set. Audit /
    /// review aid — pair with <see cref="IdentityAuditLog"/> for the
    /// before/after values.
    /// </summary>
    public DateTimeOffset? SessionLifetimeUpdatedAt { get; set; }

    /// <summary>
    /// UserName of the admin who last set <see cref="SessionLifetimeMinutes"/>.
    /// Audit / review aid — the audit log carries the same in its
    /// ChangedBy column; this is for at-a-glance "who gave Mike a
    /// 1-year session" without a join.
    /// </summary>
    public string? SessionLifetimeUpdatedBy { get; set; }

    /// <summary>
    /// Sub-classification on Team-type users — one of Field / Office /
    /// Supervisor. Stored as the <c>TeamSubtype</c> SmartEnum's name
    /// (so DB-tool reads stay human-readable).
    ///
    /// <para>
    /// Required when <see cref="UserType"/> is <c>Team</c>; must be
    /// null when <see cref="UserType"/> is <c>Tenant</c>. The invariant
    /// is enforced by <c>UserClassificationValidator</c>; both the
    /// admin endpoints and the seed reconciler call it before writing.
    /// </para>
    /// </summary>
    public string? TeamSubtype { get; set; }

    /// <summary>
    /// Hard binding to exactly one tenant for Tenant-type users —
    /// references <c>Tenants.Id</c> in the master DB (no FK because
    /// the Identity DB and master DB are separate deployments). Must
    /// be null for Team users; required for Tenant users.
    ///
    /// <para>
    /// Tenant users have access to <b>only</b> this tenant — the WebApi's
    /// <c>TeamAuthHandler</c> reads the bound tenant id off the access
    /// token's <c>tenant_id</c> claim and short-circuits the route's
    /// <c>{tenantCode}</c> check against it. Moving a Tenant user
    /// between tenants means writing this column + the security-stamp
    /// rotation that any classification change carries.
    /// </para>
    /// </summary>
    public Guid? TenantId { get; set; }
}
