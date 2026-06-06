using Domain.Enums;

namespace Domain.Entities;

public class OwaspMapping : EntityBase
{
    public Guid ScanId { get; private set; }
    public Guid FindingId { get; private set; }
    public string CategoryCode { get; private set; } = string.Empty;
    public string CategoryName { get; private set; } = string.Empty;

    public OwaspMappingStatus Status { get; private set; }

    public OwaspMappingSeverity Severity { get; private set; }
    public string FindingLabel { get; private set; } = string.Empty;

    public Scan Scan { get; private set; } = default!;
    public Finding Finding { get; private set; } = default!;

    private OwaspMapping() { }

    public static OwaspMapping Create(
        Guid scanId,
        Guid findingId,
        string categoryCode,
        string categoryName,
        OwaspMappingStatus status,
        OwaspMappingSeverity severity,
        string findingLabel)
    {
        if (scanId == Guid.Empty)
            throw new ArgumentException("ScanId cannot be empty.", nameof(scanId));

        if (findingId == Guid.Empty)
            throw new ArgumentException("FindingId cannot be empty.", nameof(findingId));

        if (string.IsNullOrWhiteSpace(categoryCode))
            throw new ArgumentException("Category code is required.", nameof(categoryCode));

        if (string.IsNullOrWhiteSpace(categoryName))
            throw new ArgumentException("Category name is required.", nameof(categoryName));

        return new OwaspMapping
        {
            ScanId = scanId,
            FindingId = findingId,
            CategoryCode = categoryCode,
            CategoryName = categoryName,
            Status = status,
            Severity = severity,
            FindingLabel = findingLabel
        };
    }
}

public enum OwaspMappingStatus
{
    COMPLIANT, PARTIAL, NON_COMPLIANT
}

public enum OwaspMappingSeverity
{
    CRITICAL, HIGH, MEDIUM, LOW, NONE
}