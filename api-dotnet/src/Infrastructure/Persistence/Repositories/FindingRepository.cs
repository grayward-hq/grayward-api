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
    public Task<List<Finding>> GetByScanId(Guid scanId, CancellationToken ct) =>
        Db.Findings
        .AsNoTracking()
            .Where(f => f.ScanId == scanId)
            .ToListAsync(ct);
    public Task<List<TrendRowDto>> GetTrendRowsByRepository(
        Guid scanId,
        DateTime since,
        CancellationToken ct)
    {
        return Db.Findings
            .AsNoTracking()
            .Where(f => f.ScanId == scanId && f.CreatedAt >= since)
            .Select(f => new TrendRowDto(
                f.Severity,
                f.CreatedAt.Date))
            .ToListAsync(ct);
    }
}