namespace SDI.Enki.Core.Abstractions;

/// <summary>
/// Ambient "who's doing this?" signal for audit and authorization. Kept
/// in Core (no AspNetCore dependency) so it can be consumed by the
/// Migrator CLI, the WebApi, and any future non-web host equally. Each
/// host registers a concrete implementation — web hosts read the OIDC
/// principal; CLI hosts return a fixed system identity.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// OIDC <c>sub</c> claim for interactive users. <c>null</c> when the
    /// caller is unauthenticated or a non-interactive process; the audit
    /// interceptor substitutes "system" in that case.
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// Friendly display name for log scopes. Can be the same as
    /// <see cref="UserId"/> if nothing better is available.
    /// </summary>
    string? UserName { get; }
}
