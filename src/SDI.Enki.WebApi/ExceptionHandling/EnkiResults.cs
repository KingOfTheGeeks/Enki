using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Infrastructure.Data;

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
    ///         dto.FromMeasured, nameof(dto.FromMeasured),
    ///         dto.ToMeasured,   nameof(dto.ToMeasured)) is { } badRange)
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

    /// <summary>
    /// Bounds an MD interval on a depth-ranged child entity (Formation,
    /// CommonMeasure, Tubular) to the well's TieOn + Survey MD envelope.
    /// Two reasons:
    ///
    /// <list type="bullet">
    ///   <item>TVD on these entities is derived by interpolating MD
    ///   against the trajectory grid (<c>ISurveyInterpolator</c>,
    ///   minimum-curvature, with the tie-on as station[0]). Outside
    ///   the bracketing range there's nothing to interpolate against,
    ///   so a value that lands here would be unresolvable.</item>
    ///   <item>The interpolator requires <i>two</i> bracketing
    ///   stations. The tie-on (auto-created on Well creation) is
    ///   station[0]; the well still needs at least one Survey row to
    ///   give the second station of the bracketing pair.</item>
    /// </list>
    ///
    /// Returns:
    /// <list type="bullet">
    ///   <item><c>null</c> when the interval is in range — caller
    ///   continues.</item>
    ///   <item>409 Conflict when the well has no Survey rows
    ///   (entity creation/edit is blocked until at least one survey
    ///   exists).</item>
    ///   <item>400 ValidationProblem keyed on the offending field
    ///   when either MD lies outside <c>[min, max]</c>, where
    ///   <c>min = min(tieOn.Depth, Surveys.Min(Depth))</c> and
    ///   <c>max = max(tieOn.Depth, Surveys.Max(Depth))</c>.</item>
    /// </list>
    /// </summary>
    public static async Task<ObjectResult?> ValidateAgainstSurveyRangeAsync(
        this ControllerBase controller,
        TenantDbContext db,
        int wellId,
        double fromMd, string fromFieldName,
        double toMd,   string toFieldName,
        CancellationToken ct)
    {
        // Tie-on is auto-created with the Well; a missing tie-on means
        // pre-invariant data. Either way the resolver also tolerates a
        // missing tie-on, so the validation here treats the well's
        // envelope as the union of (tie-on depth?) ∪ (survey depths).
        var tieOnDepth = await db.TieOns
            .AsNoTracking()
            .Where(t => t.WellId == wellId)
            .OrderBy(t => t.Id)
            .Select(t => (double?)t.Depth)
            .FirstOrDefaultAsync(ct);

        var surveyStats = await db.Surveys
            .AsNoTracking()
            .Where(s => s.WellId == wellId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Min   = g.Min(s => s.Depth),
                Max   = g.Max(s => s.Depth),
            })
            .FirstOrDefaultAsync(ct);

        var surveyCount = surveyStats?.Count ?? 0;

        // Need ≥ 1 survey on top of the tie-on so the interpolator has
        // a bracketing pair. Without surveys, the only "station" is the
        // tie-on at depth 0 — interpolation would have nothing to
        // bracket against.
        if (surveyCount < 1)
        {
            return controller.ConflictProblem(
                $"Well {wellId} needs at least one survey before this entity " +
                $"can be created or edited — vertical depth is interpolated " +
                $"from the survey grid, so the MD must fall inside the " +
                $"tie-on/survey envelope.",
                new Dictionary<string, object?>
                {
                    ["conflictKind"] = "insufficientSurveys",
                    ["surveyCount"] = surveyCount,
                });
        }

        // Build the envelope from tie-on (if present) + surveys.
        // surveyStats is non-null at this point because surveyCount >= 1.
        var min = tieOnDepth is { } td ? Math.Min(td, surveyStats!.Min) : surveyStats!.Min;
        var max = tieOnDepth is { } td2 ? Math.Max(td2, surveyStats.Max) : surveyStats.Max;

        var errors = new Dictionary<string, string[]>();
        if (fromMd < min || fromMd > max)
        {
            errors[fromFieldName] =
                [$"{fromFieldName} ({fromMd}) is outside the well's survey " +
                 $"MD range [{min}, {max}]."];
        }
        if (toMd < min || toMd > max)
        {
            errors[toFieldName] =
                [$"{toFieldName} ({toMd}) is outside the well's survey " +
                 $"MD range [{min}, {max}]."];
        }
        if (errors.Count > 0) return controller.ValidationProblem(errors);

        return null;
    }
}
