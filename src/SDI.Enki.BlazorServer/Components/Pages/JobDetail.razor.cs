using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using SDI.Enki.BlazorServer.Api;
using SDI.Enki.BlazorServer.Auth;
using SDI.Enki.Core.TenantDb.Jobs;
using SDI.Enki.Core.TenantDb.Jobs.Enums;
using SDI.Enki.Shared.Jobs;

namespace SDI.Enki.BlazorServer.Components.Pages;

[Route("/tenants/{TenantCode}/jobs/{JobId:guid}")]
[Authorize]
public partial class JobDetail : ComponentBase
{
    [Inject] public IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] public NavigationManager  Nav               { get; set; } = default!;
    [Inject] public IUserCapabilities  Capabilities      { get; set; } = default!;

    [Parameter] public string TenantCode { get; set; } = "";
    [Parameter] public Guid   JobId      { get; set; }

    [SupplyParameterFromQuery] public string? StatusError { get; set; }

    private JobDetailDto? _job;
    private string? _error;
    private string? _statusError;
    private bool _canWrite;

    /// <summary>
    /// First 8 chars of the Guid — long enough to disambiguate in a
    /// heading, short enough not to dominate the page. Full Id shows in
    /// the subtitle for copy/paste.
    /// </summary>
    private string ShortId => JobId.ToString("N")[..8];

    protected override async Task OnInitializedAsync()
    {
        _statusError = StatusError;
        var client = HttpClientFactory.CreateClient("EnkiApi");
        var result = await client.GetAsync<JobDetailDto>($"tenants/{TenantCode}/jobs/{JobId}");

        if (!result.IsSuccess)
        {
            _error = result.Error.Kind == ApiErrorKind.NotFound
                ? $"Job {ShortId} not found in tenant {TenantCode}."
                : result.Error.AsAlertText();
            return;
        }
        _job = result.Value;

        _canWrite = await Capabilities.CanWriteTenantContentAsync(TenantCode);
    }

    private static string Dashed(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
    private static string DashClass(string? s) => string.IsNullOrWhiteSpace(s) ? "enki-dash" : "";
    private static string StatusClass(string s) => s switch
    {
        "Draft"    => "enki-status-inactive",
        "Active"   => "enki-status-active",
        "Archived" => "enki-status-archived",
        _          => "",
    };

    /// <summary>
    /// Maps the DTO's string status back to the SmartEnum so we can feed
    /// <see cref="JobLifecycle.TargetsFor"/>. Null for unrecognised
    /// statuses (shouldn't happen; means Core + API drifted).
    /// </summary>
    private static JobStatus? ParseStatus(string name) =>
        JobStatus.List.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Button styling per (from, to) transition. Switching on the pair
    /// (not just the target) lets us style Restore (Archived→Active)
    /// differently from Activate (Draft→Active) — Restore is the same
    /// reversal action as Tenant's Reactivate, hence neutral styling
    /// rather than the "this is the primary path forward" primary blue.
    /// </summary>
    private static string TransitionButtonClass(JobStatus? from, JobStatus target) =>
        (from?.Name, target.Name) switch
        {
            (_,         "Archived") => "enki-button-danger",
            ("Archived", "Active")  => "",                    // Restore — neutral, mirrors Tenant Reactivate
            (_,         "Active")   => "enki-button-primary", // Activate (from Draft)
            _                       => "",
        };

    /// <summary>Inline JS confirm for destructive / scary transitions only.</summary>
    private static string TransitionConfirm(JobStatus? from, JobStatus target, string jobName) =>
        (from?.Name, target.Name) switch
        {
            (_, "Archived") => $"return confirm('Archive job {jobName}? Archived jobs become read-only until restored.');",
            _               => "",
        };

    /// <summary>
    /// Imperative verb for the button face — we're writing a command,
    /// not a status. Switches on (from, to) so Archived→Active reads as
    /// "Restore" (matches Tenant's Reactivate idiom), distinct from
    /// "Activate" (Draft→Active).
    /// </summary>
    private static string TransitionLabel(JobStatus? from, JobStatus target) =>
        (from?.Name, target.Name) switch
        {
            ("Archived", "Active") => "Restore",
            (_,          "Active") => "Activate",
            (_, "Archived")        => "Archive",
            _                      => target.Name,
        };

    /// <summary>
    /// URL segment for the lifecycle endpoint — must match the verb
    /// baked into the BlazorServer MapPost route regex and the WebApi
    /// <c>JobsController</c> route templates (<c>activate</c>,
    /// <c>archive</c>, <c>restore</c>). Restore is its own URL so audit
    /// rows carry the right verb in deploy / ops logs. Extend this
    /// switch and the regex in Program.cs when a new transition ships.
    /// </summary>
    private static string TransitionAction(JobStatus? from, JobStatus target) =>
        (from?.Name, target.Name) switch
        {
            ("Archived", "Active") => "restore",
            (_,          "Active") => "activate",
            (_, "Archived")        => "archive",
            _                      => target.Name.ToLowerInvariant(),
        };
}
