using Microsoft.EntityFrameworkCore;
using SDI.Enki.Infrastructure.Data;

namespace SDI.Enki.Infrastructure.Tests.Data;

/// <summary>
/// Checks the SQL EF Core generates for the
/// <see cref="SDI.Enki.WebApi.Controllers.GradientsController.List"/>
/// projection. The prior architecture review (#12) flagged
/// <c>g.Shots.Count</c> inside <c>.Select(...)</c> as a possible N+1
/// hazard; the follow-up review punted with "EF 10 may flatten this;
/// nobody has measured."
///
/// <para>
/// EF Core 10's relational query pipeline translates a navigation
/// <c>.Count</c> in a projection to a correlated <c>(SELECT COUNT(*) ...)</c>
/// subquery embedded in the parent <c>SELECT</c>. That's a single
/// statement to the server, not N+1. This test pins that behaviour by
/// asserting the generated SQL contains a single top-level <c>SELECT</c>
/// for the gradients query — if a future EF upgrade regresses to N+1
/// (e.g. switching to split-query default), the assertion fails loudly.
/// </para>
///
/// <para>
/// <c>ToQueryString()</c> compiles the query against the SqlServer
/// provider without opening a connection — no real database needed.
/// </para>
/// </summary>
public class GradientListSqlShapeTests
{
    private static TenantDbContext NewContext()
    {
        // Real provider so the SQL generator runs; arbitrary connection
        // string — ToQueryString never opens it.
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlServer("Server=.;Database=fake;Trusted_Connection=true")
            .Options;
        return new TenantDbContext(options);
    }

    [Fact]
    public void Gradients_List_Projection_IsSingleStatement()
    {
        using var db = NewContext();
        var runId = Guid.NewGuid();

        var query = db.Gradients
            .AsNoTracking()
            .Where(g => g.RunId == runId)
            .OrderBy(g => g.Order)
            .Select(g => new
            {
                g.Id, g.Name, g.Order, g.IsValid, g.RunId, g.ParentId, g.Timestamp,
                ShotCount = g.Shots.Count,
            });

        var sql = query.ToQueryString();

        // ApplySingularTableNames in TenantDbContext stamps every entity
        // with its CLR type name (Gradient, Shot, …) — singular, not the
        // EF default plural — so the SQL references [Gradient] / [Shot].
        //
        // One top-level SELECT means EF didn't fan out into separate
        // round-trips. Subqueries inside the projection are fine — they
        // execute on the server in the single statement.
        Assert.Equal(1, OccurrencesOf(sql, "FROM [Gradient]"));
        Assert.Contains("SELECT COUNT(*)", sql);
        Assert.Contains("[Shot]", sql);
    }

    [Fact]
    public void Gradients_Detail_Projection_IsSingleStatement()
    {
        using var db = NewContext();
        var gradientId = 42;
        var runId = Guid.NewGuid();

        var query = db.Gradients
            .AsNoTracking()
            .Where(g => g.Id == gradientId && g.RunId == runId)
            .Select(g => new
            {
                g.Id, g.Name, g.Order, g.IsValid, g.RunId, g.ParentId, g.Timestamp,
                g.Voltage, g.Frequency, g.Frame,
                ShotCount     = g.Shots.Count,
                SolutionCount = g.Solutions.Count,
                FileCount     = g.Files.Count,
                CommentCount  = g.Comments.Count,
            });

        var sql = query.ToQueryString();

        // Single top-level SELECT with four COUNT(*) subqueries.
        Assert.Equal(1, OccurrencesOf(sql, "FROM [Gradient]"));
        Assert.Equal(4, OccurrencesOf(sql, "SELECT COUNT(*)"));
    }

    private static int OccurrencesOf(string haystack, string needle) =>
        (haystack.Length - haystack.Replace(needle, "").Length) / needle.Length;
}
