namespace SDI.Enki.Shared.Audit;

/// <summary>
/// Wire shape for one audit-log row. Matches the
/// <c>AuditLog</c> tenant-DB entity field-for-field; the
/// JSON columns (<see cref="OldValues"/>, <see cref="NewValues"/>)
/// stay as raw strings on the wire — clients render them via
/// whichever JSON viewer they want, the API doesn't pre-shape.
///
/// <para>
/// <see cref="ChangedColumns"/> is the pipe-delimited list of
/// property names that actually changed on an Update; the UI tile
/// renders this as a comma-separated chip-list above the
/// before / after JSON without having to diff the two columns
/// client-side. <c>null</c> for Created (everything is new) and
/// Deleted (everything is gone).
/// </para>
/// </summary>
public sealed record AuditLogEntryDto(
    long           Id,
    string         EntityType,
    string         EntityId,
    string         Action,
    string?        OldValues,
    string?        NewValues,
    string?        ChangedColumns,
    DateTimeOffset ChangedAt,
    string         ChangedBy);
