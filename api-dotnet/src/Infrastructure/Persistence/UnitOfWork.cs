using Application.Interfaces;

namespace Infrastructure.Persistence;

public class UnitOfWork(VulnWatchDbContext db) : IUnitOfWork
{
    public async Task<T> InTransaction<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
            return await work(ct); // reuse ambient tx (e.g. a transactional MediatR behavior)

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try { var r = await work(ct); await tx.CommitAsync(ct); return r; }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    public Task SaveChanges(CancellationToken ct) => db.SaveChangesAsync(ct);
}