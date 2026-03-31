using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace OhMyLib.Repositories;

public abstract class BaseRepo<TEntity>(OhMyDbContext db) where TEntity : class
{
    protected DbSet<TEntity> EntitySet => db.Set<TEntity>();
    public IQueryable<TEntity> Query => EntitySet;
    public IQueryable<TEntity> QueryNoTracking => EntitySet.AsNoTracking();

    public virtual async ValueTask<EntityEntry<TEntity>> AddAsync(TEntity entity, CancellationToken cancellationToken = default) =>
        await EntitySet.AddAsync(entity, cancellationToken);

    public virtual EntityEntry<TEntity> Remove(TEntity entity) => EntitySet.Remove(entity);

    public virtual EntityEntry<TEntity> Update(TEntity entity) => EntitySet.Update(entity);

    public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
        await EntitySet.AnyAsync(predicate, cancellationToken);

    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        await EntitySet.AnyAsync(cancellationToken);

    public async Task<bool> AllAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
        await EntitySet.AllAsync(predicate, cancellationToken);

    public async Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
        await EntitySet.CountAsync(predicate, cancellationToken);

    public async Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        await EntitySet.CountAsync(cancellationToken);

    public async Task<long> LongCountAsync(CancellationToken cancellationToken = default) =>
        await EntitySet.LongCountAsync(cancellationToken);

    public async Task<long> LongCountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
        await EntitySet.LongCountAsync(predicate, cancellationToken);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await db.SaveChangesAsync(cancellationToken);
}