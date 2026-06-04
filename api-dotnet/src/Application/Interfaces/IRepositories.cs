using Application.Features.Domain;
using Application.Features.Scans;
using Domain.Entities;
using Domain.Enums;

namespace Application.Interfaces;

public interface IRepository<T> where T : class
{
    Task AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);
    void Remove(T entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IAlertRepository : IRepository<Alert>
{
    Task<List<Alert>> GetPendingByUser(Guid userId, int batchSize, CancellationToken ct);
    Task<List<Alert>> GetRecentByDomain(Guid domainId, int limit, CancellationToken ct);
    Task<List<Alert>> GetPendingAsync(int batchSize, CancellationToken ct);
    Task<bool> HasRecentAlert(Guid userId, AlertType type, Guid? domainId,
        TimeSpan window, CancellationToken ct);
    Task<bool> ExistsForToday(
        Guid userId, AlertType type, Guid? domainId,
        AlertChannel channel, string deduplicationKey, CancellationToken ct);
    void DetachUnsavedAlerts();
}

public interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    Task<RefreshToken?> GetById(Guid id, CancellationToken ct = default);
    Task<RefreshToken?> GetByToken(string rawToken, CancellationToken ct = default);
    Task<List<RefreshToken>> GetActiveByUserId(Guid userId, CancellationToken ct = default);

}

public interface IDomainRepository : IRepository<ScannedDomain>
{
    Task<List<ScannedDomain>> GetDomainsWithExpiringCertificates(DateTimeOffset maxLookahead, CancellationToken ct = default);
    Task<ScannedDomain?> GetById(Guid domainId, CancellationToken ct = default);
    Task<ScannedDomain?> FindUserDomainById(Guid userId, Guid domainId, CancellationToken ct);
    Task<ScannedDomain?> FindActive(string domain, CancellationToken ct);
    Task<ScannedDomain?> FindUserDomainByName(Guid userId, string domain, CancellationToken ct);
    Task<ScannedDomain?> FindUserVerifiedDomainByName(Guid userId, string domain, CancellationToken ct);
    Task<int> CountPending(Guid userId, CancellationToken ct);
    Task<ScannedDomain?> FindPendingById(Guid domainId, Guid userId, CancellationToken ct);
    public Task<ScannedDomain?> GetByNameAndUser(string domainName, Guid userId, CancellationToken ct);
    Task<(IReadOnlyList<ScannedDomain>, int)> GetPaged(DomainFilter q, CancellationToken ct = default);
}

public interface IDomainSettingsRepository
    : IRepository<DomainSettings>
{
    Task<DomainSettings?> GetByDomainId(
        Guid domainId, CancellationToken ct);

    // Used by the worker — fetch all domains due for a scan right now
    Task<List<DomainSettings>> GetDueForScan(
        DateTime asOf, int limit, CancellationToken ct);

    Task<bool> ExistsForDomain(Guid domainId, CancellationToken ct);
}

public interface IIntegrationRepository : IRepository<Integration>
{
    Task<Integration?> GetByUserAndProvider(Guid userId, IntegrationProvider provider, CancellationToken ct);
}

public interface INotificationPreferencesRepository : IRepository<NotificationPreferences>
{
    Task<bool> ExistsForUser(Guid userId, CancellationToken ct);
    Task<NotificationPreferences?> GetByUserId(Guid userId, CancellationToken ct);
}

public interface IScanRepository : IRepository<Scan>
{
    Task<Scan?> FindLatestCompletedByDomain(Guid domainId, CancellationToken ct);
    Task<Scan?> FindByIdWithFindings(Guid scanId, CancellationToken ct);
    Task<Scan?> FindRunningByDomain(Guid domainId, CancellationToken ct);
    Task<Scan?> FindByIdempotencyKey(Guid key, CancellationToken ct);
    Task<(List<Scan> Items, int TotalCount)> GetPaged(ScanFilter filter, CancellationToken ct);
}

