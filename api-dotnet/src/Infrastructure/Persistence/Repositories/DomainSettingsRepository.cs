using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class DomainSettingsRepository(VulnWatchDbContext db)
    : BaseRepository<DomainSettings>(db),
      IDomainSettingsRepository
{
    public Task<DomainSettings?> GetByDomainId(
        Guid domainId, CancellationToken ct) =>
        Db.DomainSettings
            .FirstOrDefaultAsync(s => s.DomainId == domainId, ct);

    public Task<List<DomainSettings>> GetDueForScan(
        DateTime asOf, int limit, CancellationToken ct) =>
        Db.DomainSettings
            .Include(s => s.Domain)
            .Where(s => s.MonitoringEnabled
                     && s.Domain.VerificationStatus == VerificationStatus.Verified
                     && (s.NextScheduledAt == null || s.NextScheduledAt <= asOf))
            .OrderBy(s => s.NextScheduledAt)
            .Take(limit)
            .ToListAsync(ct);

    public Task<bool> ExistsForDomain(Guid domainId, CancellationToken ct) =>
        Db.DomainSettings
            .AnyAsync(s => s.DomainId == domainId, ct);
}