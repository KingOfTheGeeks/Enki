using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace SDI.Enki.WebApi.ExceptionHandling;

/// <summary>
/// Translate <see cref="DbUpdateException"/> instances whose inner
/// cause is a SQL Server foreign-key (547) or unique-constraint (2627
/// / 2601) error into a clean 400 <c>ProblemDetails</c> the controller
/// can return. Without this, those violations propagate to the global
/// exception handler as 500 InternalServerError — the crash signature
/// on issues #26 (UPDATE FK) and #27 (INSERT FK).
///
/// <para>
/// Used as a defence-in-depth wrapper around the per-request
/// <c>SaveChangesAsync</c> in controllers that touch soft FKs we can't
/// fully validate upstream (cross-DB references, race conditions
/// between validation and save). The upstream validators
/// (<c>CalibrationFkValidation</c>, <c>ToolFkValidation</c>) still run
/// first and produce field-keyed errors; this catch is the final net
/// for everything else.
/// </para>
/// </summary>
internal static class DbUpdateFkTranslation
{
    /// <summary>
    /// Common SQL Server error numbers we want to translate:
    /// <list type="bullet">
    ///   <item>547  — FK constraint violation on INSERT/UPDATE/DELETE.</item>
    ///   <item>2601 — duplicate row in unique index (filtered).</item>
    ///   <item>2627 — duplicate row in unique index (PK / non-filtered).</item>
    /// </list>
    /// </summary>
    public static IActionResult? TryTranslate(this ControllerBase controller, DbUpdateException ex)
    {
        if (ex.InnerException is not SqlException sql) return null;

        return sql.Number switch
        {
            547 => controller.Conflict(new ProblemDetails
            {
                Title  = "Foreign-key constraint violated.",
                Detail = "The save referenced a row that doesn't exist (or whose " +
                         "delete is restricted because of this reference). " +
                         "Reload the page and verify the linked records still exist.",
                Status = StatusCodes.Status409Conflict,
                Type   = "https://enki.sdi/problems/conflict",
                Extensions =
                {
                    ["sqlNumber"] = 547,
                    ["sqlMessage"] = sql.Message,
                },
            }),
            2601 or 2627 => controller.Conflict(new ProblemDetails
            {
                Title  = "Duplicate value violated a unique constraint.",
                Detail = "Another row with the same key already exists. " +
                         "Pick different values or look up the existing row.",
                Status = StatusCodes.Status409Conflict,
                Type   = "https://enki.sdi/problems/conflict",
                Extensions =
                {
                    ["sqlNumber"] = sql.Number,
                    ["sqlMessage"] = sql.Message,
                },
            }),
            _ => null,
        };
    }
}
