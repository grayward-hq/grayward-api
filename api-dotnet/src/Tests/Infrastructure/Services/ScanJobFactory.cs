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
            scan.SurfaceTypes.ToString(),
            scan.UserId.ToString(),
            DateTime.UtcNow.ToString("O"));
    }
}