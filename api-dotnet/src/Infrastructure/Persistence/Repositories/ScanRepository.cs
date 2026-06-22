using Application.Interfaces;
using Application.Features.Domain;
using Application.Features.Scans;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Application.Features.Dashboard.DTOs;


namespace Infrastructure.Persistence.Repositories;

public sealed class ScanRepository(VulnWatchDbContext db)
    : BaseRepository<Scan>(db), IScanRepository
{
   public Task<Scan?> FindLatestCompletedByDomain(Guid domainId, CancellationToken ct) =>
        Db.Scans
            .Where(s => s.DomainId == domainId && s.Status == ScanStatus.Completed)
            .OrderByDescending(s => s.CompletedAt ?? s.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task<Scan?> FindLatestForRepository(Guid repositoryId, CancellationToken ct) =>
        Db.Scans
            .AsNoTracking()
            .Where(s => s.RepositoryId == repositoryId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task<Scan?> FindLatestCompletedForRepository(Guid repositoryId, CancellationToken ct) =>
        Db.Scans
            .AsNoTracking()
            .Where(s => s.RepositoryId == repositoryId && s.Status == ScanStatus.Completed)
            .OrderByDescending(s => s.CompletedAt)
            .Include(s => s.Repository)
            .FirstOrDefaultAsync(ct);

    public Task<Scan?> FindByIdWithFindings(Guid scanId, CancellationToken ct) =>
        Db.Scans
            .Include(s => s.Domain)
            .Include(s => s.Findings)
            .FirstOrDefaultAsync(s => s.Id == scanId, ct);
    public Task<Scan?> FindRunningByDomain(Guid domainId, CancellationToken ct) =>
        Db.Scans
            .FirstOrDefaultAsync(s =>
                s.DomainId == domainId &&
                (s.Status == ScanStatus.Queued || s.Status == ScanStatus.Running), ct);

    public Task<Scan?> FindRunningByRepoid(Guid repoId, CancellationToken ct) =>
        Db.Scans
            .FirstOrDefaultAsync(s =>
                s.RepositoryId == repoId &&
                (s.Status == ScanStatus.Queued || s.Status == ScanStatus.Running), ct);

    public Task<Scan?> FindByIdempotencyKey(Guid key, CancellationToken ct) =>
        Db.Scans
            .FirstOrDefaultAsync(s =>
                s.IdempotencyKey == key &&
                (s.Status == ScanStatus.Queued || s.Status == ScanStatus.Running), ct);

    public Task<List<ScanScoreDto>> GetRecentCompletedScans(
        Guid userId,
        DateTime daysAgo,
        CancellationToken ct) =>
        Db.Scans
            .Where(s => s.UserId == userId
                    && s.Status == ScanStatus.Completed
                    && s.CompletedAt >= daysAgo)
            .Select(s => new ScanScoreDto
            {
                CompletedAt = s.CompletedAt,
                SecurityScore = s.SecurityScore
            })
            .ToListAsync(ct);
   


    public async Task<(List<Scan> Items, int TotalCount)> GetPaged(ScanFilter filter, CancellationToken ct)
    {
        var query = Db.Scans
            .Include(s => s.Domain)
            .Where(s => s.UserId == filter.UserId);

        if (filter.DomainId.HasValue)
            query = query.Where(s => s.DomainId == filter.DomainId.Value);

        if (filter.Status.HasValue)
            query = query.Where(s => s.Status == filter.Status.Value);

        if (filter.Coverage.HasValue)
            query = query.Where(s => s.Coverage == filter.Coverage.Value);

        query = (filter.SortBy, filter.Order) switch
        {
            ("created_at", "asc") => query.OrderBy(s => s.CreatedAt),
            ("created_at", "desc") => query.OrderByDescending(s => s.CreatedAt),
            ("status", "asc") => query.OrderBy(s => s.Status),
            ("status", "desc") => query.OrderByDescending(s => s.Status),
            _ => query.OrderByDescending(s => s.CreatedAt)
        };

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public Task<int> CountUserDomainScansInPeriod(Guid userId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Db.Scans.CountAsync(s => s.UserId == userId
            && s.TargetType == ScanTargetType.Domain
            && s.CreatedAt >= from && s.CreatedAt < to
            && !(s.Status == ScanStatus.Failed), ct);

    public Task<int> CountUserRepositoryScansInPeriod(Guid userId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Db.Scans.CountAsync(s => s.UserId == userId
            && s.TargetType == ScanTargetType.Repository
            && s.CreatedAt >= from && s.CreatedAt < to
            && !(s.Status == ScanStatus.Failed), ct);

    public Task<int> CountUserActiveDomainScans(Guid userId, CancellationToken ct) =>
        Db.Scans.CountAsync(s => s.UserId == userId
            && s.TargetType == ScanTargetType.Domain
            && NonTerminalScanStatus.Contains(s.Status), ct);
    
    public Task<int> CountUserActiveRepositoryScans(Guid userId, CancellationToken ct) =>
        Db.Scans.CountAsync(s => s.UserId == userId
            && s.TargetType == ScanTargetType.Repository
            && NonTerminalScanStatus.Contains(s.Status), ct);

     private static readonly ScanStatus[] NonTerminalScanStatus =
        { ScanStatus.Queued, ScanStatus.Running };

}

