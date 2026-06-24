using Domain.Enums;

namespace Application.Features.Repository.DTOs;

public record RepoSummary(
    Guid Id,
    string FullName,
    string CloneUrl,
    string DefaultBranch,
    bool IsPrivate,
    RepositoryStatus Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? LastScannedAt);