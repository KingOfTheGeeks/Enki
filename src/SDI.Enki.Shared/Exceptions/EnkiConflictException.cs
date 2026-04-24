namespace SDI.Enki.Shared.Exceptions;

/// <summary>
/// Thrown when the requested state transition or write conflicts with
/// the current persisted state (e.g. deactivating an already-archived
/// tenant, or a unique-index violation the domain wants to report
/// deliberately rather than surface as a 500). Maps to HTTP 409.
/// </summary>
public sealed class EnkiConflictException : EnkiException
{
    public EnkiConflictException(string message) : base(message) { }
    public EnkiConflictException(string message, Exception? inner) : base(message, inner) { }

    public override int HttpStatusCode => StatusCodes.Status409Conflict;
    public override string ProblemType => "https://enki.sdi/problems/conflict";
}
