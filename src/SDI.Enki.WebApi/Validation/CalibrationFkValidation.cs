using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.WebApi.Validation;

/// <summary>
/// Soft-FK existence check for <c>Shot.CalibrationId</c> /
/// <c>Log.CalibrationId</c> against the tenant <c>Calibrations</c>
/// table. Lets controllers fail fast with a clean 400
/// <c>ValidationProblemDetails</c> instead of letting SQL Server
/// raise <c>DbUpdateException</c> (FK constraint 547) which the
/// global handler maps to 500 — the crash signature on issue #26.
///
/// <para>
/// Belt-and-braces once the corresponding Blazor edit pages move to
/// a constrained dropdown of valid calibrations; still useful as a
/// server-side guard for callers that bypass the form (direct API
/// hits, custom scripts).
/// </para>
/// </summary>
internal static class CalibrationFkValidation
{
    /// <summary>
    /// Returns <c>null</c> when <paramref name="calibrationId"/> is null
    /// (the "no calibration yet" path) or when it resolves to a real
    /// row in <c>db.Calibrations</c>; otherwise returns a 400
    /// <c>ValidationProblemDetails</c> with a field error keyed on
    /// <c>"calibrationId"</c> so the form surfaces it next to the
    /// Calibration input.
    /// </summary>
    public static async Task<IActionResult?> ValidateCalibrationIdAsync(
        this ControllerBase controller,
        TenantDbContext db,
        int? calibrationId,
        CancellationToken ct)
    {
        if (calibrationId is null) return null;

        var exists = await db.Calibrations
            .AsNoTracking()
            .AnyAsync(c => c.Id == calibrationId, ct);
        if (exists) return null;

        return controller.ValidationProblem(new ValidationProblemDetails(
            new Dictionary<string, string[]>
            {
                ["calibrationId"] =
                [
                    $"Calibration #{calibrationId} was not found in this tenant. " +
                    "Pick an existing calibration from the dropdown, or leave blank for none.",
                ],
            }));
    }
}
