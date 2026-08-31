using Application.Features.Scans.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;

namespace Tests.Infrastructure.Services;

public sealed class FakeScanJobFactory : IScanJobFactory
{
    public ScanJob Create(Scan scan)
    {
        return new ScanJob(
            scan.DomainId?.ToString() ?? "",
            "",
            scan.RepositoryId?.ToString() ?? "",
            scan.Id.ToString(),
            scan.Coverage.ToString(),
            // Mirrors the real ScanJobFactory: a mask expands to one name per surface.
            Enum.GetValues<global::Domain.Enums.SurfaceType>()
                .Where(v => v != global::Domain.Enums.SurfaceType.None && scan.SurfaceTypes.HasFlag(v))
                .Select(v => v.ToString())
                .ToList(),
            scan.UserId.ToString(),
            DateTime.UtcNow.ToString("O"));
    }
}