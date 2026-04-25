using SDI.Enki.Shared.Paging;

namespace SDI.Enki.WebApi.Tests.Paging;

/// <summary>
/// Trivial behaviour pin for <see cref="PagedResult{T}.HasMore"/>.
/// The paging envelope is consumed by every admin grid, so a regression
/// in the HasMore arithmetic would silently cap pagination across the
/// whole admin surface.
/// </summary>
public class PagedResultTests
{
    [Theory]
    [InlineData(0,   100, 100, 1000, true)]   // first page of ten
    [InlineData(900, 100, 100, 1000, false)]  // last page exact-fit
    [InlineData(950, 50,  50,  1000, false)]  // last page partial
    [InlineData(0,   100, 50,  50,   false)]  // total smaller than page
    [InlineData(0,   0,   100, 0,    false)]  // empty result set
    public void HasMore_ReflectsRemainingRows(int skip, int itemCount, int take, int total, bool expected)
    {
        var items  = Enumerable.Range(0, itemCount).Select(i => $"item-{i}").ToList();
        var result = new PagedResult<string>(items, total, skip, take);

        Assert.Equal(expected, result.HasMore);
    }
}
