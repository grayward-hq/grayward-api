using Application.Features.Scans.DTOs;
using Application.Interfaces;
using Domain.Entities;

namespace Application.Mappers;

public sealed class ScanJobFactory : IScanJobFactory
{
    public ScanJob Create(Scan scan)
    {
        return new ScanJob(
            DomainId: scan.DomainId?.ToString() ?? string.Empty,
            DomainName: scan.Domain?.DomainName ?? string.Empty,
            RepoId: scan.RepositoryId?.ToString() ?? string.Empty,
            ScanId: scan.Id.ToString(),
            ScanType: scan.Coverage.ToString(),
            SurfaceType: scan.SurfaceTypes.ToString(),
            RequestedBy: scan.UserId.ToString(),
            EnqueuedAt: DateTimeOffset.UtcNow.ToString("O")
        );
    }
}