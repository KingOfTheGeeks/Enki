using SDI.Enki.Core.Abstractions;

namespace SDI.Enki.Infrastructure.Auditing;

/// <summary>
/// Fallback <see cref="ICurrentUser"/> for contexts without an
/// authenticated principal — the Migrator CLI, design-time tooling,
/// unit tests. Audit fields stamp with "system" so the timeline in
/// the DB still makes sense even for machine-driven writes.
///
/// WebApi / Blazor register their own HttpContext-backed implementation
/// that wins at DI resolve time via the last-registration-wins rule.
/// </summary>
internal sealed class SystemCurrentUser : ICurrentUser
{
    public string? UserId   => "system";
    public string? UserName => "system";
}
