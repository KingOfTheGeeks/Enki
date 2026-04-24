namespace SDI.Enki.Shared.Exceptions;

/// <summary>
/// Thrown from domain code when a rule fails that can't be expressed
/// as a DataAnnotation on the DTO (cross-field rules, external-system
/// consistency checks, etc.). Maps to HTTP 400 with an <c>errors</c>
/// dictionary in the ProblemDetails extensions.
///
/// For pure DTO validation (Required / MaxLength / RegularExpression),
/// prefer the DataAnnotations attributes on the DTO — <c>[ApiController]</c>
/// returns a 400 automatically before the controller action even runs.
/// </summary>
public sealed class EnkiValidationException : EnkiException
{
    public EnkiValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public EnkiValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = new[] { error } }) { }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public override int HttpStatusCode => StatusCodes.Status400BadRequest;
    public override string ProblemType => "https://enki.sdi/problems/validation";

    public override IReadOnlyDictionary<string, object?> Extensions => new Dictionary<string, object?>
    {
        ["errors"] = Errors,
    };
}
