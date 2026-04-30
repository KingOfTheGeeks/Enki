using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.WebApi.Validation;

/// <summary>
/// Soft-FK existence check for <c>Run.ToolId</c> against the master
/// <c>Tools</c> table. Tool lives in the master DB; Run lives in the
/// tenant DB. SQL Server doesn't constrain across databases, so
/// validation is at the application layer.
///
/// <para>
/// Sibling of <see cref="CalibrationFkValidation"/> — same shape,
/// different soft-FK target. Returns a 400 ValidationProblemDetails
/// keyed on <c>"toolId"</c> when the supplied id doesn't resolve, so
/// the form surfaces it next to the Tool dropdown.
/// </para>
/// </summary>
internal static class ToolFkValidation
{
    /// <summary>
    /// Returns <c>null</c> when <paramref name="toolId"/> is null
    /// (the "no tool yet" path) or when it resolves to a real row in
    /// <c>master.Tools</c>; otherwise returns a 400
    /// <c>ValidationProblemDetails</c>.
    /// </summary>
    public static async Task<IActionResult?> ValidateToolIdAsync(
        this ControllerBase controller,
        EnkiMasterDbContext master,
        Guid? toolId,
        CancellationToken ct)
    {
        if (toolId is null) return null;

        var exists = await master.Tools
            .AsNoTracking()
            .AnyAsync(t => t.Id == toolId, ct);
        if (exists) return null;

        return controller.ValidationProblem(new ValidationProblemDetails(
            new Dictionary<string, string[]>
            {
                ["toolId"] =
                [
                    $"Tool {toolId} was not found in the master fleet registry. " +
                    "Pick an existing tool from the dropdown.",
                ],
            }));
    }
}
