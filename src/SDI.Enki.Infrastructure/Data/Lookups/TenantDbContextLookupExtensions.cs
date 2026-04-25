using System.Linq.Expressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace SDI.Enki.Infrastructure.Data.Lookups;

/// <summary>
/// Find-or-create semantics for lookup-style entities (Magnetics, Calibration,
/// LoggingSetting, etc.) where the same natural-key value is expected to be
/// referenced by many parent rows and duplicates must collapse to one row.
///
/// Replaces the 17 AFTER-INSERT dedup triggers from legacy Athena. The
/// DB-level UNIQUE INDEX on each lookup's natural key is the backstop if a
/// race slips through between the find and the create.
///
/// Written as an extension on <see cref="TenantDbContext"/> rather than a
/// DI service so callers use whatever context instance they built via
/// <c>ITenantDbContextFactory</c> — no scoped TenantDbContext is needed in
/// the container.
/// </summary>
public static class TenantDbContextLookupExtensions
{
    /// <summary>
    /// SQL Server error numbers for "row would duplicate a unique
    /// constraint" — 2627 (PK / UNIQUE CONSTRAINT) and 2601 (UNIQUE
    /// INDEX). Either fires when two callers race the insert under a
    /// natural-key UNIQUE INDEX.
    /// </summary>
    private const int SqlPrimaryKeyViolation = 2627;
    private const int SqlDuplicateUniqueIdx  = 2601;

    /// <summary>
    /// Returns the Id of an existing row whose natural key matches the
    /// sample, or inserts the sample and returns its new Id. Race-safe:
    /// when two callers concurrently miss the lookup and try to insert,
    /// the loser's <see cref="DbUpdateException"/> is caught, the failed
    /// entity is detached, and a re-query returns the winner's Id. The
    /// DB-level UNIQUE INDEX on the natural key is the backstop the
    /// catch arms — without it this method would silently store
    /// duplicates under racing inserts.
    /// </summary>
    /// <typeparam name="TEntity">Lookup entity type (e.g. Magnetics).</typeparam>
    /// <typeparam name="TId">Type of the primary key (e.g. <c>int</c>).</typeparam>
    /// <param name="db">The tenant context to find / insert through.</param>
    /// <param name="sample">
    /// An unsaved instance carrying the desired natural-key values. If no
    /// match is found, this is the entity inserted.
    /// </param>
    /// <param name="match">
    /// IQueryable predicate that selects rows equal to <paramref name="sample"/>
    /// by natural key. Kept as an expression so it translates to SQL.
    /// </param>
    /// <param name="getId">Reads the primary-key value off an entity.</param>
    public static async Task<TId> FindOrCreateAsync<TEntity, TId>(
        this TenantDbContext db,
        TEntity sample,
        Expression<Func<TEntity, bool>> match,
        Func<TEntity, TId> getId,
        CancellationToken ct = default)
        where TEntity : class
    {
        var existing = await db.Set<TEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(match, ct);

        if (existing is not null)
            return getId(existing);

        db.Set<TEntity>().Add(sample);
        try
        {
            await db.SaveChangesAsync(ct);
            return getId(sample);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Race: another caller inserted between our find and our
            // save. Detach the failed sample so the change tracker
            // doesn't hold the doomed row, then re-query for the winner.
            db.Entry(sample).State = EntityState.Detached;

            var winner = await db.Set<TEntity>()
                .AsNoTracking()
                .FirstOrDefaultAsync(match, ct);

            // Defensive: if the winner isn't visible yet (e.g. a delete
            // raced the insert), surface the original exception with
            // context rather than throw a confusing NRE.
            if (winner is null)
                throw new InvalidOperationException(
                    $"FindOrCreateAsync<{typeof(TEntity).Name}>: insert failed " +
                    "with a unique-violation but no matching row could be re-queried. " +
                    "Likely a delete raced the insert.", ex);

            return getId(winner);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql
        && (sql.Number == SqlPrimaryKeyViolation
         || sql.Number == SqlDuplicateUniqueIdx);
}
