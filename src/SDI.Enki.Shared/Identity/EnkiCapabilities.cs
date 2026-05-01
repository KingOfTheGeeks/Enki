namespace SDI.Enki.Shared.Identity;

/// <summary>
/// Atomic capability grants stored as <c>AspNetUserClaims</c> rows
/// (claim type = <see cref="EnkiClaimTypes.Capability"/>). Capabilities
/// are <b>orthogonal to <c>TeamSubtype</c></b> — they're per-user
/// elevations for a single class of operation that the user's subtype
/// wouldn't otherwise grant. Today's first member, <see cref="Licensing"/>,
/// lets a non-Supervisor (e.g., a trusted Office user) generate / revoke
/// licenses without the rest of Supervisor's privileges.
///
/// <para>
/// Conceptually a Team-side construct only — the validator rejects
/// capability grants on Tenant-type users.
/// </para>
///
/// <para>
/// Adding a new capability: add a constant here + add it to
/// <see cref="All"/>. The admin UI's "Special permissions" section
/// auto-renders a checkbox per <see cref="All"/> entry.
/// </para>
/// </summary>
public static class EnkiCapabilities
{
    /// <summary>
    /// Grants license generation and revocation regardless of TeamSubtype.
    /// Combined OR with the Supervisor subtype gate in
    /// <c>CanManageLicensing</c>.
    /// </summary>
    public const string Licensing = "licensing";

    /// <summary>
    /// All known capabilities. Drives the admin UI's checkbox list and
    /// the validator's allow-list when checking a grant request. Order
    /// is wire-stable (admin UI renders in this order).
    /// </summary>
    public static readonly IReadOnlyList<string> All = [Licensing];

    /// <summary>
    /// True if <paramref name="capability"/> is a known capability name.
    /// Used by the admin endpoints to reject grant requests for typos
    /// before they reach the claim store.
    /// </summary>
    public static bool IsKnown(string? capability) =>
        !string.IsNullOrEmpty(capability) && All.Contains(capability, StringComparer.Ordinal);
}

/// <summary>
/// Stable claim type names used across Identity + WebApi + Blazor.
/// Centralised here so a typo in one host can't drift the wire format
/// silently — same rationale as <see cref="AuthConstants"/>.
/// </summary>
public static class EnkiClaimTypes
{
    /// <summary>
    /// Capability claim type. Value is one of <see cref="EnkiCapabilities.All"/>.
    /// Persisted in <c>AspNetUserClaims</c>; emitted on the principal by
    /// the base <c>UserClaimsPrincipalFactory</c>.
    /// </summary>
    public const string Capability = "enki:capability";
}
