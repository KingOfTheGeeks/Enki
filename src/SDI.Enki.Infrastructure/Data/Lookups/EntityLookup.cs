using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace SDI.Enki.Infrastructure.Data.Lookups;

/// <summary>
/// Default <see cref="IEntityLookup{TEntity}"/> implementation bound to a
/// <see cref="TenantDbContext"/>. Transient / scoped lifetime: never cache
/// instances across requests because the context is request-scoped.
/// </summary>
public sealed class EntityLookup<TEntity>(TenantDbContext db) : IEntityLookup<TEntity>
    where TEntity : class
{
    public async Task<TId> FindOrCreateAsync<TId>(
        TEntity sample,
        Expression<Func<TEntity, bool>> match,
        Func<TEntity, TId> getId,
        CancellationToken ct = default)
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
