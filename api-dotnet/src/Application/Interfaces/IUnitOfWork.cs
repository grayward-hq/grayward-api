namespace Application.Interfaces;

public interface IUnitOfWork
{
    Task<T> InTransaction<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);
    Task SaveChanges(CancellationToken ct);
}