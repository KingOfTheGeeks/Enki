namespace SDI.Enki.Core.Abstractions;

/// <summary>
/// Entities implementing this get automatic audit-field population by
/// the DbContext's SaveChangesAsync override:
///
///   • On insert — CreatedAt + CreatedBy are stamped from the current
///     HttpContext principal (or "system" in non-web contexts).
///   • On update — UpdatedAt + UpdatedBy are stamped the same way.
///
/// <see cref="RowVersion"/> is an EF Core concurrency token; if two
/// writers race on the same row the second one throws
/// <c>DbUpdateConcurrencyException</c> which the exception handler maps
/// to an HTTP 409.
///
/// Do not set these properties manually from business code — the
/// interceptor owns them. Reading them from queries is fine.
/// </summary>
public interface IAuditable
{
    DateTimeOffset   CreatedAt   { get; set; }
    string?          CreatedBy   { get; set; }
    DateTimeOffset?  UpdatedAt   { get; set; }
    string?          UpdatedBy   { get; set; }
    byte[]?          RowVersion  { get; set; }
}
