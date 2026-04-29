using Microsoft.AspNetCore.Authorization;

namespace SDI.Enki.WebApi.Authorization;

/// <summary>
/// System-admin-only requirement. Caller must hold the
/// <c>enki-admin</c> role claim. Replaces the previous
/// <c>RequireAssertion(ctx => ctx.User.HasEnkiAdminRole())</c>
/// inline assertion so denials can be audited via
/// <see cref="IAuthzDenialAuditor"/> alongside the tenant-scoped
/// requirements.
///
/// <para>
/// The role check itself is unchanged: <see cref="PrincipalExtensions.HasEnkiAdminRole"/>
/// reads the <c>role</c> claim type that OpenIddict tokens emit.
/// </para>
/// </summary>
public sealed class EnkiAdminOnlyRequirement : IAuthorizationRequirement;

public sealed class EnkiAdminOnlyHandler(
    IAuthzDenialAuditor denialAuditor,
    ILogger<EnkiAdminOnlyHandler> logger)
    : AuthorizationHandler<EnkiAdminOnlyRequirement>
{
    private const string Name = nameof(EnkiAdminOnlyHandler);

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        EnkiAdminOnlyRequirement requirement)
    {
        if (context.User.HasEnkiAdminRole())
        {
            context.Succeed(requirement);
            return;
        }

        // Capture the actor so the audit row attributes the denial.
        // sub is AspNetUsers.Id; this mirrors the actor field shape on
        // the other two handlers' audit writes.
        var sub = context.User.FindFirst("sub")?.Value ?? "(unknown)";

        logger.LogInformation(
            "{Handler} denied: caller {Sub} does not hold the enki-admin role.",
            Name, sub);

        await denialAuditor.RecordAsync(
            policy:     EnkiPolicies.EnkiAdminOnly,
            tenantCode: null,
            actorSub:   sub,
            reason:     "NotEnkiAdmin");
    }
}
