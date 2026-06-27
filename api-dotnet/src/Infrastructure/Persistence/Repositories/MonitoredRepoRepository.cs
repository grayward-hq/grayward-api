using Application.Interfaces;
using Application.Features.Domain;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Application.Features.Integrations.GitHub.DTOs;
using Application.Features.Repository;

namespace Infrastructure.Persistence.Repositories;

public sealed class MonitoredRepoRepository(VulnWatchDbContext db)
    : BaseRepository<MonitoredRepository>(db), IMonitoredRepoRepository
{
    public Task<int> CountUserRepositories(Guid userId, CancellationToken ct) =>
        Db.MonitoredRepositories.CountAsync(r => r.UserId == userId, ct);

    public Task<List<MonitoredRepository>> GetByUserId(Guid userId, CancellationToken ct) =>
        Db.MonitoredRepositories
             .Where(r =>
                r.UserId == userId)
            .ToListAsync(ct);

    public Task<MonitoredRepository?> GetUserRepoByRepoId(Guid userId, Guid repositoryId, CancellationToken ct) =>
        Db.MonitoredRepositories
            .Include(r => r.Settings)
            .FirstOrDefaultAsync(r => r.Id == repositoryId && r.UserId == userId, ct);

    public Task<List<MonitoredRepository>> GetByInstallationId(string installationId, CancellationToken ct) => 
        Db.MonitoredRepositories
             .Where(r =>
                r.InstallationId == installationId)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<MonitoredRepository>, int)> GetPaged(RepoFilter filter, CancellationToken ct = default)
    {
        var query = Db.MonitoredRepositories
            .AsNoTracking()
            .Where(d => d.UserId == filter.UserId);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(d => d.FullName.Contains(filter.Search));

        if (filter.Status.HasValue)
            query = query.Where(d => d.Status == filter.Status.Value);

        var totalCount = await query.CountAsync(ct);

        query = (filter.SortBy, filter.Order) switch
        {
            ("name", "asc") => query.OrderBy(p => p.FullName),
            ("name", "desc") => query.OrderByDescending(p => p.FullName),
            ("status", "asc") => query.OrderBy(p => p.Status),
            ("status", "desc") => query.OrderByDescending(p => p.Status),
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