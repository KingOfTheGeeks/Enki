using System.Reflection;
using Microsoft.AspNetCore.RateLimiting;
using SDI.Enki.WebApi.Controllers;

namespace SDI.Enki.WebApi.Tests.Pipeline;

/// <summary>
/// Pins the per-action <c>[EnableRateLimiting("Expensive")]</c>
/// wiring on the two endpoints expensive enough to warrant a
/// throttle — tenant provisioning (creates two databases, applies
/// migrations, seeds data — multi-second latency per call) and
/// survey import (reads up to 20 MB, runs minimum-curvature on
/// every row). The policy lives in <c>Program.cs</c>:
/// fixed-window, 5 requests / minute / partitioned-by-user.
///
/// <para>
/// We don't drive the actual 429 end-to-end here — the policy
/// enforcement is Microsoft's responsibility and would need
/// <c>WebApplicationFactory</c> + 6 sequential calls to verify.
/// What we own is "is the attribute on the right action with the
/// right policy name", which reflection covers cheaply.
/// </para>
/// </summary>
public class RateLimitAttributeTests
{
    private const string ExpensivePolicyName = "Expensive";

    private static EnableRateLimitingAttribute? FindEnableRateLimitingAttribute(
        Type controller, string actionName) =>
        controller.GetMethod(actionName)
                 ?.GetCustomAttribute<EnableRateLimitingAttribute>(inherit: false);

    [Theory]
    [InlineData(typeof(TenantsController), nameof(TenantsController.Provision))]
    [InlineData(typeof(SurveysController), nameof(SurveysController.Import))]
    public void ExpensiveActions_AreDecoratedWithRateLimitingPolicy(
        Type controller, string actionName)
    {
        var attr = FindEnableRateLimitingAttribute(controller, actionName);
        Assert.NotNull(attr);
        Assert.Equal(ExpensivePolicyName, attr!.PolicyName);
    }

    // Sanity: a regular endpoint should NOT carry the rate-limit
    // attribute. List + Get are the canonical counter-examples — they
    // hit caches / lightweight queries.
    [Theory]
    [InlineData(typeof(TenantsController), nameof(TenantsController.List))]
    [InlineData(typeof(SurveysController), nameof(SurveysController.List))]
    [InlineData(typeof(WellsController),   nameof(WellsController.Get))]
    public void RegularActions_AreNotDecoratedWithRateLimit(
        Type controller, string actionName)
    {
        var attr = FindEnableRateLimitingAttribute(controller, actionName);
        Assert.Null(attr);
    }
}
