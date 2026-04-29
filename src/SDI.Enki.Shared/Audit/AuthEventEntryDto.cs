namespace SDI.Enki.Shared.Audit;

/// <summary>
/// Wire shape for one auth-event row (sign-in / sign-out / token /
/// lockout). Distinct from <see cref="AuditLogEntryDto"/> because
/// the audit-log envelope (entity-mutation snapshots) doesn't fit
/// authentication events — there's no entity ID to follow, no
/// before / after JSON. The fields here mirror the
/// <c>AuthEventLog</c> entity in the Identity host.
///
/// <para>
/// <see cref="Detail"/> is the event-specific JSON payload — failure
/// reason for <c>SignInFailed</c>, grant type for <c>TokenIssued</c>;
/// null otherwise. Clients render it as a tooltip / expand-row chip.
/// </para>
/// </summary>
public sealed record AuthEventEntryDto(
    long           Id,
    string         EventType,
    string         Username,
    string?        IdentityId,
    string?        IpAddress,
    string?        UserAgent,
    string?        Detail,
    DateTimeOffset OccurredAt);
