using Application.Features.Scans.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;

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
            ScanType: scan.TargetType.ToString(),
            SurfaceTypes: DecomposeSurfaceTypes(scan.SurfaceTypes),
            RequestedBy: scan.UserId.ToString(),
            EnqueuedAt: scan.CreatedAt.ToString("O")
        );
    }

    /// <summary>
    /// Expands the bitmask into the individual surface names the worker expects.
    /// </summary>
    /// <remarks>
    /// This previously sent scan.SurfaceTypes.ToString() under a singular "SurfaceType" key, so the
    /// worker - which reads a JSON array from "SurfaceTypes" - saw null and fell back to running
    /// every scanner. The mask could not have been decomposed correctly anyway while Http = 3
    /// overlapped Dns | Ssl.
    ///
    /// Member names are emitted verbatim; the worker's SurfaceType.fromString matches on its own
    /// enum names case-insensitively, and the two enums are kept name-for-name identical.
    /// </remarks>
    private static List<string> DecomposeSurfaceTypes(SurfaceType flags) =>
        Enum.GetValues<SurfaceType>()
            .Where(value => value != SurfaceType.None && flags.HasFlag(value))
            .Select(value => value.ToString())
            .ToList();
}