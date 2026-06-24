using Application.Interfaces;
using Application.Features.Domain;
using Application.Features.Scans;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Application.Features.Dashboard.DTOs;
using Application.Features.Repository.DTOs;

namespace Infrastructure.Persistence.Repositories;

public sealed class FindingRepository(VulnWatchDbContext db)
    : BaseRepository<Finding>(db), IFindingRepository
{
    public Task<List<VulnerabilityListItemDto>> GetByScanId(Guid scanId, CancellationToken ct) =>
        Db.Findings
        .AsNoTracking()
            .Where(f => f.ScanId == scanId)
            .Select(f => new VulnerabilityListItemDto(
                f.Id,
                f.Title,
                f.Severity,
                f.AiExplanation,
                f.CveId,
                f.Status,
                f.CreatedAt))
            .ToListAsync(ct);
    public Task<List<TrendRowDto>> GetTrendRowsByRepository(
        Guid repositoryId,
        DateTime since,
        CancellationToken ct)
    {
        return Db.Findings
            .AsNoTracking()
            .Where(f => f.Scan.RepositoryId == repositoryId && 
             f.Scan.Status == ScanStatus.Completed &&
             f.CreatedAt >= since)
            .Select(f => new TrendRowDto(
                f.Severity,
                f.CreatedAt.Date))
            .ToListAsync(ct);
    }
}