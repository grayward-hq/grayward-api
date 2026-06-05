namespace Application.Features.Compliance.DTOs;

public record OwaspEvaluationDto(
    Guid ScanId,
    int OverallScore,
    string ComplianceTier,
    List<OwaspCategoryDto> Categories
)
{
    public int CompliantCount => Categories.Count(c => c.ComplianceStatus == "Compliant");
    public int ThreatCount => Categories.Count(c => c.ComplianceStatus == "NonCompliant");
};

