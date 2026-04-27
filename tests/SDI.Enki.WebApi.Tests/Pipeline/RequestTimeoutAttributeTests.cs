using System.Reflection;
using Microsoft.AspNetCore.Http.Timeouts;
using SDI.Enki.WebApi.Controllers;

namespace SDI.Enki.WebApi.Tests.Pipeline;

/// <summary>
/// Pins the per-action <c>[RequestTimeout("LongRunning")]</c>
/// wiring on the three long-running endpoints — survey import,
/// survey force-recalc, and the travelling-cylinder anti-collision
/// scan. The attribute names a policy that
/// <c>Program.cs</c> registers via
/// <c>AddRequestTimeouts(o =&gt; o.AddPolicy("LongRunning", ...))</c>;
/// without the attribute on the action, the policy never fires.
///
/// <para>
/// We don't drive the actual timeout end-to-end here — that needs
/// <c>WebApplicationFactory</c> + a deliberately-slow handler, and
/// the framework's enforcement is Microsoft's responsibility. What
/// we own and need to pin is that the attribute is present, named
/// correctly, and on the actions the architecture review identified
/// as expensive enough to need a wall-clock cap.
/// </para>
/// </summary>
public class RequestTimeoutAttributeTests
{
    private const string LongRunningPolicyName = "LongRunning";

    private static RequestTimeoutAttribute? FindRequestTimeoutAttribute(
        Type controller, string actionName) =>
        controller.GetMethod(actionName)
                 ?.GetCustomAttribute<RequestTimeoutAttribute>(inherit: false);

    [Theory]
    [InlineData(typeof(SurveysController), nameof(SurveysController.Calculate))]
    [InlineData(typeof(SurveysController), nameof(SurveysController.Import))]
    [InlineData(typeof(WellsController),   nameof(WellsController.AntiCollision))]
    public void LongRunningActions_AreDecoratedWithRequestTimeoutPolicy(
        Type controller, string actionName)
    {
        var attr = FindRequestTimeoutAttribute(controller, actionName);
        Assert.NotNull(attr);
        Assert.Equal(LongRunningPolicyName, attr!.PolicyName);
    }

    // Sanity: a controller action that's NOT long-running should not
    // carry the attribute. The list endpoint is the canonical
    // counter-example — fast, cached, no need for a special timeout.
    [Theory]
    [InlineData(typeof(SurveysController), nameof(SurveysController.List))]
    [InlineData(typeof(WellsController),   nameof(WellsController.List))]
    [InlineData(typeof(WellsController),   nameof(WellsController.Get))]
    public void RegularActions_AreNotDecoratedWithRequestTimeout(
        Type controller, string actionName)
    {
        var attr = FindRequestTimeoutAttribute(controller, actionName);
        Assert.Null(attr);
    }
}
