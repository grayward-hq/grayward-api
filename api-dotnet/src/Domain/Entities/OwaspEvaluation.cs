namespace Domain.Entities;

public class OwaspEvaluation : EntityBase
{
    public Guid ScanId { get; private set; }
    public Guid DomainId { get; private set; }
    public int OverallScore { get; private set; }
    public string ComplianceTier { get; private set; } = default!;
    public ICollection<OwaspCategoryScore> CategoryScores { get; private set; } = new List<OwaspCategoryScore>();
    
    public static OwaspEvaluation Create(Guid scanId, Guid domainId, int overallScore, string tier)
        => new() { ScanId = scanId, DomainId = domainId, OverallScore = overallScore, ComplianceTier = tier };
    
    public void SetScore(int score, string tier)
    {
        OverallScore = score;
        ComplianceTier = tier;
        Touch();
    }
}