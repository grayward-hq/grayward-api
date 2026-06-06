namespace Application.Features.Compliance.DTOs;

public record OwaspEvaluationResult(
    int OverallScore,
    string ComplianceTier,
    List<OwaspCategoryResult> Categories
);
