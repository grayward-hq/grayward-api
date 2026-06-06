namespace Application.Features.Compliance.DTOs;

public record OwaspCategoryDto(
    string Code,
    string Name,
    int Score,
    string ComplianceStatus,
    int FindingCount
);

