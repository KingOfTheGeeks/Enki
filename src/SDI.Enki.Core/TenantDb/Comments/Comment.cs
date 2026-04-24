using SDI.Enki.Core.TenantDb.Shots;

namespace SDI.Enki.Core.TenantDb.Comments;

/// <summary>
/// Unified comment entity. Replaces legacy Athena's three parallel tables
/// (<c>Comments</c>, <c>RotaryComments</c>, <c>PassiveComments</c>) — all had
/// identical shape and differed only in which junction pointed at them.
///
/// Three junctions remain (GradientComment, RotaryComment, PassiveComment)
/// and all point at this single Comments table via EF skip-navigation
/// many-to-many relationships.
/// </summary>
public class Comment(string text, string user)
{
    public int Id { get; set; }

    public string Text { get; set; } = text;

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Display name of the commenter.</summary>
    public string User { get; set; } = user;

    /// <summary>AspNetUsers.Id (Identity DB) when the commenter is a system user.</summary>
    public Guid? Identity { get; set; }

    // EF navs — three target types share this single Comment table.
    public ICollection<Gradient> Gradients { get; set; } = new List<Gradient>();
    public ICollection<Rotary> Rotaries { get; set; } = new List<Rotary>();
    public ICollection<Passive> Passives { get; set; } = new List<Passive>();
}
