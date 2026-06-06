using Application.Interfaces;

namespace Infrastructure.Persistence.Repositories;

public abstract class BaseRepository<T>(VulnWatchDbContext db) : IRepository<T> where T : class
{
    protected readonly VulnWatchDbContext Db = db;

    public Task AddAsync(T entity, CancellationToken ct = default) =>
        Db.Set<T>().AddAsync(entity, ct).AsTask();

    public void Update(T entity) =>
        Db.Set<T>().Update(entity);

    public void Remove(T entity) =>
        Db.Set<T>().Remove(entity);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);
}