using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SDI.Enki.Shared.Exceptions;

namespace SDI.Enki.WebApi.ExceptionHandling;

/// <summary>
/// Global exception handler registered via <c>AddExceptionHandler</c> +
/// <c>UseExceptionHandler</c>. Maps:
/// <list type="bullet">
///   <item><c>EnkiException</c> subclasses — typed status + stable ProblemType +
///   merged Extensions (from the exception's <c>Extensions</c> dictionary).</item>
///   <item><c>DbUpdateConcurrencyException</c> — 409 Conflict with a fixed
///   ProblemType so clients can detect "someone else edited this row".</item>
///   <item>Anything else — 500 with a generic problem type. Logged as Error;
///   message is echoed in Development only.</item>
/// </list>
///
/// The correlation id (<c>HttpContext.TraceIdentifier</c>, which the
/// correlation middleware overwrites with an inbound or newly-minted
/// X-Request-Id) rides along as the <c>traceId</c> extension on every
/// response so users reporting an error can paste it and we can go
/// straight to the log line.
/// </summary>
public sealed class EnkiExceptionHandler(
    ILogger<EnkiExceptionHandler> logger,
    IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, type, title, detail, extensions) = Map(exception, env.IsDevelopment());

        // Log level split: 5xx is our bug, 4xx is the client's problem —
        // still log so ops can spot patterns but only at Warning.
        if (status >= 500)
            logger.LogError(exception,
                "Unhandled exception: {ExceptionType} — {Message}",
                exception.GetType().Name, exception.Message);
        else
            logger.LogWarning(exception,
                "Handled domain exception: {ExceptionType} → HTTP {Status}",
                exception.GetType().Name, status);

        // RetryLimitExceededException wraps the actual transient SQL fault
        // after the SqlServerRetryingExecutionStrategy gives up. The
        // wrapper's message is opaque ("max retries exceeded"); the inner
        // is the real cause (4060, 40197, 40501, 40613, etc.). Surface
        // the SQL error number + server explicitly so the next time this
        // fires we don't need a debugger to see it.
        if (exception is RetryLimitExceededException retry &&
            retry.InnerException is SqlException sql)
        {
            logger.LogError(
                "RetryLimitExceeded inner SqlException: Number={Number}, " +
                "Server={Server}, Message={SqlMessage}",
                sql.Number, sql.Server, sql.Message);
        }

        var problem = EnkiProblem.Build(httpContext, status, type, title, detail, extensions);

        httpContext.Response.StatusCode  = status;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    private static (
        int Status,
        string Type,
        string Title,
        string Detail,
        IReadOnlyDictionary<string, object?> Extensions
    ) Map(Exception ex, bool isDev) => ex switch
    {
        EnkiException enki => (
            enki.HttpStatusCode,
            enki.ProblemType,
            enki.GetType().Name.Replace("Exception", string.Empty),
            enki.Message,
            enki.Extensions),

        DbUpdateConcurrencyException => (
            StatusCodes.Status409Conflict,
            "https://enki.sdi/problems/concurrency",
            "ConcurrencyConflict",
            "The record was modified by another user. Reload and try again.",
            EmptyExtensions),

        _ => (
            StatusCodes.Status500InternalServerError,
            "https://enki.sdi/problems/internal-error",
            "InternalServerError",
            isDev ? ex.Message : "An unexpected error occurred. Check logs for the trace id.",
            EmptyExtensions),
    };

    private static readonly IReadOnlyDictionary<string, object?> EmptyExtensions
        = new Dictionary<string, object?>(0);
}
