using Domain.Entities;
using Domain.Enums;

namespace Application.Features.Repository.DTOs;

public record RepoDetailDto(
    Guid RepositoryId,
    string FullName,
    string HtmlUrl,
    string DefaultBranch,
    bool IsPrivate,
    RepoSettingsDto Settings,
    ScanStatus? LatestScanStatus,
    DateTime? LastScanAt,  
    IReadOnlyList<SeverityCountDto> OpenBySeverity,
    IReadOnlyList<VulnerabilityListItemDto> Vulnerabilities,
    IReadOnlyList<TrendPointDto> Trend);    