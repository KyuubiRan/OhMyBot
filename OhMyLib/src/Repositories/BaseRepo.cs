using Microsoft.EntityFrameworkCore;

namespace OhMyLib.Repositories;

public abstract class BaseRepo<TEntity>(OhMyDbContext db) where TEntity : class, new()
{
    public DbSet<TEntity> EntitySet => db.Set<TEntity>();

    public TEntity? FindById(long id) => EntitySet.Find(id);

    public List<TEntity> GetAll() => EntitySet.ToList();

    public List<TEntity> GetAll(int offset, int size) => EntitySet.Skip(offset).Take(size).ToList();

    public ValueTask<TEntity?> FindByIdAsync(long id) => EntitySet.FindAsync(id);

    public virtual void Add(TEntity entity) => EntitySet.Add(entity);

    public virtual void Remove(TEntity entity) => EntitySet.Remove(entity);

    public virtual void Update(TEntity entity) => EntitySet.Update(entity);

    public virtual void SaveChanges() => db.SaveChanges();
}