namespace Domain.Entities;

public class OwaspCategoryScore : EntityBase
{
    public Guid EvaluationId { get; private set; }
    public string CategoryCode { get; private set; } = default!; // "A01", "A02" etc.
    public string CategoryName { get; private set; } = default!;
    public int Score { get; private set; }
    public string ComplianceStatus { get; private set; } = default!; // Compliant | PartiallyCompliant | NonCompliant
}