using Microsoft.AspNetCore.Mvc;

namespace SDI.Enki.WebApi.ExceptionHandling;

/// <summary>
/// <see cref="ControllerBase"/> extensions for the expected-failure paths
/// (404 / 409 / 400 on known-bad input). Returns a typed
/// <see cref="ObjectResult"/> with the same ProblemDetails body shape the
/// global handler produces — no exception, no debugger break on the
/// expected case, same wire format as unhandled-exception responses.
///
/// Reserve <see cref="SDI.Enki.Shared.Exceptions.EnkiException"/> throws
/// for layers deeper than the controller (Infrastructure services) where
/// returning a result type doesn't plumb cleanly. Controllers themselves
/// know their HTTP status at the decision site — say so directly.
/// </summary>
public static class EnkiResults
{
    public const string NotFoundType   = "https://enki.sdi/problems/not-found";
    public const string ConflictType   = "https://enki.sdi/problems/conflict";
    public const string ValidationType = "https://enki.sdi/problems/validation";

    public static ObjectResult NotFoundProblem(
        this ControllerBase controller,
        string entityKind,
        string entityKey)
    {
        var problem = EnkiProblem.Build(
            controller.HttpContext,
            status: StatusCodes.Status404NotFound,
            type:   NotFoundType,
            title:  "NotFound",
            detail: $"{entityKind} '{entityKey}' not found.",
            extensions: new Dictionary<string, object?>
            {
                ["entityKind"] = entityKind,
                ["entityKey"]  = entityKey,
            });

        return new ObjectResult(problem)
        {
            StatusCode   = StatusCodes.Status404NotFound,
            ContentTypes = { "application/problem+json" },
        };
    }

    public static ObjectResult ConflictProblem(
        this ControllerBase controller,
        string reason,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var problem = EnkiProblem.Build(
            controller.HttpContext,
            status: StatusCodes.Status409Conflict,
            type:   ConflictType,
            title:  "Conflict",
            detail: reason,
            extensions: extensions);

        return new ObjectResult(problem)
        {
            StatusCode   = StatusCodes.Status409Conflict,
            ContentTypes = { "application/problem+json" },
        };
    }

    public static ObjectResult ValidationProblem(
        this ControllerBase controller,
        IReadOnlyDictionary<string, string[]> errors)
    {
        var problem = EnkiProblem.Build(
            controller.HttpContext,
            status: StatusCodes.Status400BadRequest,
            type:   ValidationType,
            title:  "Validation",
            detail: "One or more validation errors occurred.",
            extensions: new Dictionary<string, object?>
            {
                ["errors"] = errors,
            });

        return new ObjectResult(problem)
        {
            StatusCode   = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    }

    /// <summary>
    /// Range check for the <c>From*</c> / <c>To*</c> depth pair on
    /// CommonMeasure / Formation / Tubular / similar interval-shaped
    /// DTOs. Returns <c>null</c> when <paramref name="fromValue"/> is
    /// <c>&lt;=</c> <paramref name="toValue"/>; otherwise returns a
    /// 400 <see cref="ValidationProblem"/> keyed on
    /// <paramref name="fromFieldName"/> with a uniform error message.
    /// Caller propagates the result with <c>return badRange;</c>:
    /// <code>
    /// if (this.ValidateDepthRange(
    ///         dto.FromVertical, nameof(dto.FromVertical),
    ///         dto.ToVertical,   nameof(dto.ToVertical)) is { } badRange)
    ///     return badRange;
    /// </code>
    /// </summary>
    public static ObjectResult? ValidateDepthRange(
        this ControllerBase controller,
        double fromValue, string fromFieldName,
        double toValue,   string toFieldName)
    {
        if (fromValue <= toValue) return null;

        return controller.ValidationProblem(new Dictionary<string, string[]>
        {
            [fromFieldName] =
                [$"{fromFieldName} ({fromValue}) must be less than or equal to {toFieldName} ({toValue})."],
        });
    }
}
