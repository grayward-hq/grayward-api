using Application.Interfaces;
using Application.Features.Domain;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class DomainRepository(VulnWatchDbContext db)
    : BaseRepository<ScannedDomain>(db), IDomainRepository
{
    public Task<List<ScannedDomain>> GetDomainsWithExpiringCertificates(DateTimeOffset maxLookahead, CancellationToken ct = default) =>
        Db.Domains
            .Where(d =>
                d.VerificationStatus == VerificationStatus.Verified &&
                d.SslCertExpiry != null &&
                d.SslCertExpiry > DateTimeOffset.UtcNow &&
                d.SslCertExpiry <= maxLookahead)
            .ToListAsync(ct);
    
    public Task<ScannedDomain?> GetById(Guid domainId, CancellationToken ct = default) =>
         Db.Domains
             .FirstOrDefaultAsync(d => d.Id == domainId, ct);

    public Task<ScannedDomain?> FindUserDomainById(Guid userId, Guid domainId, CancellationToken ct) =>
        Db.Domains
            .Include(d => d.Scans
                .Where(s => s.Status == ScanStatus.Completed)
                .OrderByDescending(s => s.CompletedAt)
                .Take(1))
            .FirstOrDefaultAsync(d => d.Id == domainId && d.UserId == userId, ct);
    public Task<ScannedDomain?> FindActive(string domain, CancellationToken ct) =>
        Db.Domains
            .FirstOrDefaultAsync(d =>
                d.DomainName == domain &&
                d.VerificationStatus != VerificationStatus.Revoked, ct);

    public Task<ScannedDomain?> FindUserDomainByName(Guid userId, string domain, CancellationToken ct) =>
        Db.Domains
        .FirstOrDefaultAsync(d =>
        d.UserId == userId &&
        d.DomainName == domain, ct);

    public Task<ScannedDomain?> FindUserVerifiedDomainByName(Guid userId, string domain, CancellationToken ct) =>
        Db.Domains
        .FirstOrDefaultAsync(d =>
        d.UserId == userId &&
        d.DomainName == domain &&
        d.VerificationStatus == VerificationStatus.Verified, ct);

    public Task<int> CountPending(Guid userId, CancellationToken ct) =>
        Db.Domains
            .CountAsync(d =>
                d.UserId == userId &&
                d.VerificationStatus == VerificationStatus.Pending, ct);

    public Task<ScannedDomain?> FindPendingById(Guid id, Guid userId, CancellationToken ct) =>
        Db.Domains
            .FirstOrDefaultAsync(d =>
                d.Id == id &&
                d.UserId == userId &&
                d.VerificationStatus == VerificationStatus.Pending, ct);

    public Task<ScannedDomain?> GetByNameAndUser(string domainName, Guid userId, CancellationToken ct) =>
        Db.Domains
            .FirstOrDefaultAsync(d =>
                d.DomainName == domainName &&
                d.UserId == userId, ct);

    public async Task<(IReadOnlyList<ScannedDomain>, int)> GetPaged(DomainFilter filter, CancellationToken ct = default)
    {
        var query = Db.Domains
            .AsNoTracking()
            .Where(d => d.UserId == filter.UserId);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(d => d.DomainName.Contains(filter.Search));

        if (filter.Status.HasValue)
            query = query.Where(d => d.VerificationStatus == filter.Status.Value);

        var totalCount = await query.CountAsync(ct);

        query = (filter.SortBy, filter.Order) switch
        {
            ("domain", "asc") => query.OrderBy(p => p.DomainName),
            ("domain", "desc") => query.OrderByDescending(p => p.DomainName),
            ("status", "asc") => query.OrderBy(p => p.VerificationStatus),
            ("status", "desc") => query.OrderByDescending(p => p.VerificationStatus),
            ("created_at", "desc") => query.OrderByDescending(p => p.CreatedAt),
            _ => query.OrderBy(p => p.CreatedAt),
        };

        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Include(d => d.Scans
                .Where(s => s.Status == ScanStatus.Completed)
                .OrderByDescending(s => s.CompletedAt)
                .Take(1))
            .ToListAsync(ct);

        return (items, totalCount);
    }

}