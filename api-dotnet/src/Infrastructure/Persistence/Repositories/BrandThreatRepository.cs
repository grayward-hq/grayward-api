using Application.Interfaces;
using Application.Features.Domain;
using Application.Features.BrandProtection.DTOs;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class BrandThreatRepository(VulnWatchDbContext db)
    : BaseRepository<BrandThreat>(db), IBrandThreatRepository
{
    public Task<List<BrandThreat>> FindActiveByDomainNotInList(
    Guid domainId,
    List<string> checkedCandidates,
    CancellationToken ct = default)
    {
        var candidates = checkedCandidates
            .Select(x => x.ToLower())
            .ToList();

        return Db.BrandThreats
            .Where(t =>
                t.DomainId == domainId &&
                t.Status == BrandThreatStatus.Active &&
                !candidates.Contains(t.LookAlikeDomain.ToLower()))
            .ToListAsync(ct);
    }

    public Task<BrandThreat?> FindByDomainAndLookAlike(
    Guid domainId,
    string lookAlike,
    CancellationToken ct = default) =>
    Db.BrandThreats
        .FirstOrDefaultAsync(
            t => t.DomainId == domainId &&
                 t.LookAlikeDomain == lookAlike,
            ct);

    public Task<List<BrandThreat>> FindActiveByDomain(
        Guid domainId,
        CancellationToken ct = default) =>
        Db.BrandThreats
            .Where(t =>
                t.DomainId == domainId &&
                t.Status == BrandThreatStatus.Active)
            .ToListAsync(ct);

    public Task<List<BrandThreat>> FindByDomain(
        Guid domainId,
        CancellationToken ct = default) =>
        Db.BrandThreats
            .Where(t => t.DomainId == domainId)
            .ToListAsync(ct);

    public async Task<(List<BrandThreat> Items, int TotalCount)> GetPagedByDomain(
    Guid domainId,
    BrandThreatStatus? status,
    BrandThreatRiskLevel? riskLevel,
    int page,
    int pageSize,
    CancellationToken ct)
    {
        var query = Db.BrandThreats
            .Where(t => t.DomainId == domainId);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        if (riskLevel.HasValue)
            query = query.Where(t => t.RiskLevel == riskLevel.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(t => t.RiskLevel)   // High → Medium → Low
            .ThenByDescending(t => t.LastCheckedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task<BrandThreat?> FindByIdAndDomain(Guid threatId, Guid domainId, CancellationToken ct) =>
    Db.BrandThreats
        .Include(t => t.Domain)
        .FirstOrDefaultAsync(t => t.Id == threatId && t.DomainId == domainId, ct);

    public async Task<BrandThreatSummary> GetSummaryByDomain(Guid domainId, CancellationToken ct)
    {
        var counts = await Db.BrandThreats
            .Where(t => t.DomainId == domainId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Active = g.Count(t => t.Status == BrandThreatStatus.Active),
                Resolved = g.Count(t => t.Status == BrandThreatStatus.Resolved),
                Monitoring = g.Count(t => t.Status == BrandThreatStatus.Monitoring),
            })
            .FirstOrDefaultAsync(ct);

        return counts is null
            ? new BrandThreatSummary(0, 0, 0, 0)
            : new BrandThreatSummary(counts.Total, counts.Active, counts.Resolved, counts.Monitoring);
    }
}