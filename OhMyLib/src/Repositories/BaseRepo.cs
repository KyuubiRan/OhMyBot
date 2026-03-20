using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace OhMyLib.Repositories;

public abstract class BaseRepo<TEntity>(OhMyDbContext db) where TEntity : class, new()
{
    public DbSet<TEntity> EntitySet => db.Set<TEntity>();

    public ValueTask<TEntity?> FindByIdAsync(long id, CancellationToken cancellationToken = default) => EntitySet.FindAsync([id], cancellationToken);

    public async Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default) => await EntitySet.ToListAsync(cancellationToken);

    public async Task<List<TEntity>> GetAllAsync(int offset, int size, CancellationToken cancellationToken = default) =>
        await EntitySet.Skip(offset).Take(size).ToListAsync(cancellationToken);

    public virtual async ValueTask<EntityEntry<TEntity>> AddAsync(TEntity entity, CancellationToken cancellationToken = default) =>
        await EntitySet.AddAsync(entity, cancellationToken);

    public virtual EntityEntry<TEntity> Remove(TEntity entity) => EntitySet.Remove(entity);

    public virtual EntityEntry<TEntity> Update(TEntity entity) => EntitySet.Update(entity);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await db.SaveChangesAsync(cancellationToken);
}