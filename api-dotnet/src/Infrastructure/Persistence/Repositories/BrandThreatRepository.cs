using Application.Interfaces;
using Application.Features.Domain;
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
}