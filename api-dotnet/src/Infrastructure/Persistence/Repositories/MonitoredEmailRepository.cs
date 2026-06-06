using Application.Interfaces;
using Application.Features.Domain;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Application.Features.BreachMonitoring.DTOs;

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

    public async Task<(List<MonitoredEmail> Items, int TotalCount)> GetPagedByDomain(
    Guid domainId,
    bool? isBreached,
    int page,
    int pageSize,
    CancellationToken ct)
    {
        var query = Db.MonitoredEmails
            .Where(e => e.DomainId == domainId);

        if (isBreached.HasValue)
            query = query.Where(e => e.IsBreached == isBreached.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(e => e.IsBreached)   // breached first
            .ThenByDescending(e => e.BreachCount)
            .ThenBy(e => e.EmailAddress)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<MonitoredEmailSummary> GetSummaryByDomain(
        Guid domainId, CancellationToken ct)
    {
        var counts = await Db.MonitoredEmails
            .Where(e => e.DomainId == domainId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Breached = g.Count(e => e.IsBreached),
                NotBreached = g.Count(e => !e.IsBreached),
            })
            .FirstOrDefaultAsync(ct);

        return counts is null
            ? new MonitoredEmailSummary(0, 0, 0)
            : new MonitoredEmailSummary(counts.Total, counts.Breached, counts.NotBreached);
    }

}