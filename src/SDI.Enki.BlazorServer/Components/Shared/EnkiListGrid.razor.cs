using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Grids;

namespace SDI.Enki.BlazorServer.Components.Shared;

public partial class EnkiListGrid<TValue> : ComponentBase
{
    /// <summary>Row source. Re-AutoFit fires on every bind, so filter / refresh re-fits.</summary>
    [Parameter] public IEnumerable<TValue>? DataSource { get; set; }

    /// <summary>
    /// Field name of the column that should stay elastic (no Width, absorbs leftover container
    /// width). Typically the first / link / human-readable identifier column. Required.
    /// </summary>
    [Parameter, EditorRequired] public string ElasticField { get; set; } = "";

    /// <summary>The page's <c>&lt;GridColumn&gt;</c> declarations.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public bool AllowSorting   { get; set; } = true;
    [Parameter] public bool AllowFiltering { get; set; } = true;
    [Parameter] public bool AllowResizing  { get; set; } = true;
    [Parameter] public bool AllowPaging    { get; set; }

    /// <summary>Initial page size when <see cref="AllowPaging"/> is true. Default 10.</summary>
    [Parameter] public int PageSize { get; set; } = 10;

    /// <summary>SfGrid Height. Default <c>"auto"</c> — grid sizes to its content.</summary>
    [Parameter] public string Height { get; set; } = "auto";

    private SfGrid<TValue>? _grid;

    /// <summary>
    /// Fired by SfGrid after each data bind. AutoFits every field-bound
    /// column except <see cref="ElasticField"/>; the elastic column is
    /// left unconstrained so it absorbs the container's leftover width.
    /// </summary>
    private async Task OnDataBoundAsync()
    {
        if (_grid?.Columns is not { } columns) return;

        var fields = columns
            .Select(c => c.Field)
            .Where(f => !string.IsNullOrEmpty(f) && f != ElasticField)
            .ToArray();

        if (fields.Length > 0)
        {
            await _grid.AutoFitColumnsAsync(fields);
        }
    }
}
