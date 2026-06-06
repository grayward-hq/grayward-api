using Domain.Enums;

namespace Domain.Entities;

public class Scan : EntityBase
{
    public Guid UserId { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public ScanTargetType TargetType { get; private set; }
    public ScanCoverage Coverage { get; private set; }

    public SurfaceType SurfaceTypes { get; private set; }
    public Guid? DomainId { get; private set; }
    public Guid? RepositoryId { get; private set; }
    public ScanStatus Status { get; private set; }
    public int? SecurityScore { get; private set; }
    public int? OWASPScore { get; private set; }
    public string? OWASPTier { get; private set; }
    public string? OWASPPostureSummary  { get; private set; }

    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public ScannedDomain? Domain { get; private set; }
    public MonitoredRepository? Repository { get; private set; }
    public User User { get; private set; } = default!;
    public ICollection<Finding> Findings { get; private set; } = new List<Finding>();

    private Scan() { }

    public static Scan Create(
        Guid userId,
        Guid idempotencyKey,
        ScanTargetType targetType,
        ScanCoverage coverage,
        SurfaceType surfaceTypes,
        Guid? domainId = null,
        Guid? repositoryId = null)
        => new()
        {
            UserId = userId,
            IdempotencyKey = idempotencyKey,
            TargetType = targetType,
            Coverage = coverage,
            SurfaceTypes = surfaceTypes,
            DomainId = domainId,
            RepositoryId = repositoryId,
            Status = ScanStatus.Queued,
        };
    public void MarkRunning()
    {
        Status = ScanStatus.Running;
        StartedAt = DateTime.UtcNow;
        Touch();
    }

    public void Complete(int securityScore)
    {
        Status = ScanStatus.Completed;
        SecurityScore = securityScore;
        CompletedAt = DateTime.UtcNow;
        Touch();
    }

    public void Fail()
    {
        Status = ScanStatus.Failed;
        CompletedAt = DateTime.UtcNow;
        Touch();
    }
}
