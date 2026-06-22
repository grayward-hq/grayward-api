namespace Application.Features.Scans.DTOs;

// public record ScanJob(Guid DomainId, string DomainName, Guid ScanId, string ScanType, string SurfaceType, Guid RequestedBy, DateTime EnqueuedAt)
// {
//     public static ScanJob Create(Guid domainId, string domainName, Guid scanId, string scanType, string surfaceType, Guid requestedBy, DateTime enqueuedAt) => new(domainId, domainName, scanId, scanType, surfaceType, requestedBy, enqueuedAt);
// }
public sealed record ScanJob(
    string DomainId,
    string DomainName,
    string RepoId,
    string ScanId,
    string ScanType,
    string SurfaceType,
    string RequestedBy,
    string EnqueuedAt
);