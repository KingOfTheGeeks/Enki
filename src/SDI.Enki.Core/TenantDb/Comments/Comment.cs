using SDI.Enki.Core.TenantDb.Shots;

namespace SDI.Enki.Core.TenantDb.Comments;

/// <summary>
/// User-authored comment attached to a <see cref="Shot"/>.
///
/// <para>
/// <b>Phase 2 reshape:</b> Comments were previously many-to-many
/// against <c>Gradient</c> / <c>Rotary</c> / <c>Passive</c> via three
/// junction tables. Those parent entities are deleted in Phase 2;
/// the Comment entity reparents to <see cref="Shot"/> as a 1:N child
/// (each Comment belongs to exactly one Shot; each Shot can have
/// many Comments). Simpler than the m:n shape — comments are about
/// specific captures, not run-grouping abstractions.
/// </para>
///
/// <para>
/// If a need for run-level (not shot-level) commentary arrives,
/// add <c>RunId</c> as a second nullable FK with a CHECK enforcing
/// exactly-one-non-null. Not in this slice.
/// </para>
/// </summary>
public class Comment(int shotId, string text, string user)
{
    public int Id { get; set; }

    /// <summary>Parent Shot. Required.</summary>
    public int ShotId { get; set; } = shotId;

    public string Text { get; set; } = text;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Display name of the commenter.</summary>
    public string User { get; set; } = user;

    /// <summary>AspNetUsers.Id (Identity DB) when the commenter is a system user.</summary>
    public Guid? Identity { get; set; }

    // EF nav
    public Shot? Shot { get; set; }
}
