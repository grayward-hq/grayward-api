using Domain.Entities;

namespace Application.Features.Compliance.DTOs;

public record OwaspCategoryResult(
    string Code,           // "A01", "A02", etc.
    string Name,           // "Broken Access Control", etc.
    int Score,             // 0–100
    string ComplianceStatus, // "Compliant" | "PartiallyCompliant" | "NonCompliant"
    List<Finding> Findings, // the actual findings that mapped to this category
   List<string> TechnicalDetails);