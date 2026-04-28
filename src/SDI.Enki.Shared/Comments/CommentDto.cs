namespace SDI.Enki.Shared.Comments;

/// <summary>
/// One comment row. Phase 2 reshape — Comments now attach 1:N to a
/// Shot (was m:n with Gradient/Rotary/Passive in the legacy shape).
/// </summary>
public sealed record CommentDto(
    int             Id,
    int             ShotId,
    string          Text,
    string          User,
    Guid?           Identity,
    DateTimeOffset  Timestamp);
