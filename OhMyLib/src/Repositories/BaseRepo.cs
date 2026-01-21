using Microsoft.EntityFrameworkCore;

namespace OhMyLib.Repositories;

public abstract class BaseRepo<TEntity>(OhMyDbContext db) where TEntity : class, new()
{
    public DbSet<TEntity> EntitySet => db.Set<TEntity>();

    public TEntity? FindById(long id) => EntitySet.Find(id);

    public ValueTask<TEntity?> FindByIdAsync(long id, CancellationToken cancellationToken = default) => EntitySet.FindAsync([id], cancellationToken);

    public List<TEntity> GetAll() => EntitySet.ToList();

    public async ValueTask<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default) => await EntitySet.ToListAsync(cancellationToken);

    public List<TEntity> GetAll(int offset, int size) => EntitySet.Skip(offset).Take(size).ToList();

    public async ValueTask<List<TEntity>> GetAllAsync(int offset, int size, CancellationToken cancellationToken = default) =>
        await EntitySet.Skip(offset).Take(size).ToListAsync(cancellationToken);

    public virtual void Add(TEntity entity) => EntitySet.Add(entity);

    public virtual async ValueTask AddAsync(TEntity entity, CancellationToken cancellationToken = default) =>
        await EntitySet.AddAsync(entity, cancellationToken);

    public virtual void Remove(TEntity entity) => EntitySet.Remove(entity);

    public virtual void Update(TEntity entity) => EntitySet.Update(entity);

    public void SaveChanges() => db.SaveChanges();

    public async ValueTask<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await db.SaveChangesAsync(cancellationToken);
}