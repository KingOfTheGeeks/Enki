using Microsoft.AspNetCore.Components;

namespace SDI.Enki.BlazorServer.Components.Audit;

public partial class AuditActionBadge : ComponentBase
{
    [Parameter, EditorRequired] public string Action { get; set; } = "";

    private string PillClass => Action switch
    {
        "Created" => "enki-status-active",
        "Updated" => "enki-status-inactive",
        "Deleted" => "enki-status-archived",
        "Denied"  => "enki-status-inactive",
        _         => "",
    };
}
