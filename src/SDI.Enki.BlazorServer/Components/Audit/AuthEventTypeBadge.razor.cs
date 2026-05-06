using Microsoft.AspNetCore.Components;

namespace SDI.Enki.BlazorServer.Components.Audit;

public partial class AuthEventTypeBadge : ComponentBase
{
    [Parameter, EditorRequired] public string EventType { get; set; } = "";

    private string PillClass => EventType switch
    {
        "SignInSucceeded"  => "enki-status-active",
        "TokenIssued"      => "enki-status-archived",
        "SignOut"          => "enki-status-archived",
        "SignInFailed"     => "enki-status-inactive",
        "LockoutTriggered" => "enki-status-inactive",
        _                  => "",
    };
}
