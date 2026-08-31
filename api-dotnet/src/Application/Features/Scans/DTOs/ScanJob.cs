namespace Application.Features.Scans.DTOs;

public sealed record ScanJob(
    string DomainId,
    string DomainName,
    string RepoId,
    string ScanId,
    string ScanType,
    List<string> SurfaceTypes,
    string RequestedBy,
    string EnqueuedAt
);