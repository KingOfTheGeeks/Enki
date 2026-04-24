namespace SDI.Enki.Shared.Exceptions;

/// <summary>
/// Base class for Enki's domain exceptions. The global
/// <c>EnkiExceptionHandler</c> maps each subclass to a consistent
/// RFC 7807 <c>ProblemDetails</c> response using <see cref="HttpStatusCode"/>,
/// <see cref="ProblemType"/>, and <see cref="Extensions"/>. Any other
/// unhandled exception surfaces as a 500.
///
/// Throw a specific subclass from controllers / services; do not throw
/// this base type directly.
/// </summary>
public abstract class EnkiException : Exception
{
    protected EnkiException(string message) : base(message) { }
    protected EnkiException(string message, Exception? inner) : base(message, inner) { }

    /// <summary>HTTP status code the exception maps to.</summary>
    public abstract int HttpStatusCode { get; }

    /// <summary>
    /// Stable URI identifying the problem kind. Clients may key off this
    /// instead of the title, which is allowed to change.
    /// </summary>
    public abstract string ProblemType { get; }

    /// <summary>
    /// Extra key/value pairs merged into the ProblemDetails payload as
    /// top-level extensions (per RFC 7807 §3.2). Override to surface
    /// structured detail beyond the message string — the exception
    /// handler reads this verbatim.
    /// </summary>
    public virtual IReadOnlyDictionary<string, object?> Extensions { get; }
        = new Dictionary<string, object?>();
}
