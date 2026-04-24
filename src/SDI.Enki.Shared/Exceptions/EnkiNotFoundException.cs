namespace SDI.Enki.Shared.Exceptions;

/// <summary>
/// Thrown when a requested entity does not exist. Maps to HTTP 404.
/// Extensions carry the entity kind + key so clients can react to
/// "TenantCode not found" vs "JobId not found" without parsing the
/// human-readable message.
/// </summary>
public sealed class EnkiNotFoundException : EnkiException
{
    public EnkiNotFoundException(string entityKind, string entityKey)
        : base($"{entityKind} '{entityKey}' not found.")
    {
        EntityKind = entityKind;
        EntityKey = entityKey;
    }

    public string EntityKind { get; }
    public string EntityKey  { get; }

    public override int HttpStatusCode => StatusCodes.Status404NotFound;
    public override string ProblemType => "https://enki.sdi/problems/not-found";

    public override IReadOnlyDictionary<string, object?> Extensions => new Dictionary<string, object?>
    {
        ["entityKind"] = EntityKind,
        ["entityKey"]  = EntityKey,
    };
}

// Minimal StatusCodes mirror so SDI.Enki.Shared stays free of an
// AspNetCore package reference. Values match Microsoft.AspNetCore.Http.StatusCodes.
internal static class StatusCodes
{
    public const int Status400BadRequest  = 400;
    public const int Status401Unauthorized = 401;
    public const int Status403Forbidden   = 403;
    public const int Status404NotFound    = 404;
    public const int Status409Conflict    = 409;
    public const int Status500InternalServerError = 500;
}
