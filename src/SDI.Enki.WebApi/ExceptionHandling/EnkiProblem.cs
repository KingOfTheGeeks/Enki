using Microsoft.AspNetCore.Mvc;

namespace SDI.Enki.WebApi.ExceptionHandling;

/// <summary>
/// Shared builder for Enki's <see cref="ProblemDetails"/> responses.
///
/// Both <see cref="EnkiExceptionHandler"/> (genuine unhandled exceptions)
/// and <see cref="EnkiResults"/> (controller-side expected failures like
/// NotFound / Conflict) go through this so every error response has the
/// same JSON shape — same top-level fields, same <c>traceId</c>, same
/// type URI convention.
/// </summary>
public static class EnkiProblem
{
    public static ProblemDetails Build(
        HttpContext http,
        int status,
        string type,
        string title,
        string detail,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status   = status,
            Type     = type,
            Title    = title,
            Detail   = detail,
            Instance = http.Request.Path,
        };
        problem.Extensions["traceId"] = http.TraceIdentifier;

        if (extensions is not null)
        {
            foreach (var (k, v) in extensions)
                problem.Extensions[k] = v;
        }
        return problem;
    }
}
