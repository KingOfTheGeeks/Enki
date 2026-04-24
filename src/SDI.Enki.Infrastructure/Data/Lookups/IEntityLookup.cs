namespace SDI.Enki.Infrastructure.Data.Lookups;

/// <summary>
/// Find-or-create semantics for lookup-style entities (Magnetics, Calibrations,
/// LoggingSettings, etc.) where the same natural-key value is expected to be
/// referenced by many parent rows and duplicates must collapse to one row.
///
/// Replaces the 17 AFTER-INSERT dedup triggers from legacy Athena. Writers
/// call <see cref="FindOrCreateAsync"/> before inserting dependent records;
/// the DB-level UNIQUE INDEX on the natural key is the backstop if a race
/// slips through.
/// </summary>
public interface IEntityLookup<TEntity> where TEntity : class
{
    /// <summary>
    /// Returns the Id of an existing row whose natural key matches the
    /// sample, or inserts the sample and returns its new Id. The match
    /// predicate is supplied by the caller so the lookup stays generic.
    /// </summary>
    /// <param name="sample">
    /// An unsaved instance carrying the desired natural-key values.
    /// If no match is found, this is the entity that will be inserted.
    /// </param>
    /// <param name="match">
    /// IQueryable predicate that selects rows equal to <paramref name="sample"/>
    /// by natural key. Kept as an expression so it can translate to SQL.
    /// </param>
    /// <param name="getId">Reads the primary-key value off an entity.</param>
    Task<TId> FindOrCreateAsync<TId>(
        TEntity sample,
        System.Linq.Expressions.Expression<Func<TEntity, bool>> match,
        Func<TEntity, TId> getId,
        CancellationToken cancellationToken = default);
}
