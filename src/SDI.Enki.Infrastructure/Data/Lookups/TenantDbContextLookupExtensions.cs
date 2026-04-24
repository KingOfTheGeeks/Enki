using System.Linq.Expressions;
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
    /// Returns the Id of an existing row whose natural key matches the
    /// sample, or inserts the sample and returns its new Id.
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
        await db.SaveChangesAsync(ct);
        return getId(sample);
    }
}
