using Application.Interfaces;
using Application.Features.Domain;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class MonitoredEmailRepository(VulnWatchDbContext db)
    : BaseRepository<MonitoredEmail>(db), IMonitoredEmailRepository
{
    public Task<List<MonitoredEmail>> GetByDomainId(Guid domainId, CancellationToken ct) =>
        Db.MonitoredEmails
            .Where(d => d.DomainId == domainId)
            .ToListAsync(ct);

   public Task<List<MonitoredEmail>> FindByUser(Guid userId, CancellationToken ct) =>
    Db.MonitoredEmails
            .Where(d => d.UserId == userId)
            .ToListAsync(ct);
    public Task<MonitoredEmail?> FindByDomainAndEmail(Guid domainId, string email, CancellationToken ct) => 
     Db.MonitoredEmails
      .FirstOrDefaultAsync(
            t => t.DomainId == domainId &&
                 t.EmailAddress == email,
            ct);
    public Task<int> CountByDomain(Guid domainId, CancellationToken ct) =>
     Db.MonitoredEmails
            .CountAsync(d =>
                d.DomainId == domainId, ct);

    public Task<MonitoredEmail?> FindById(Guid emailId, CancellationToken ct) =>
     Db.MonitoredEmails
      .FirstOrDefaultAsync(
            t => t.Id == emailId, ct);
    
}