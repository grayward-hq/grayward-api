namespace Domain.Entities;

public class MonitoredEmail : EntityBase
{
    public Guid UserId { get; private set; }
    public Guid DomainId { get; private set; }
    public string EmailAddress { get; private set; } = default!;
    public bool IsBreached { get; private set; }
    public int BreachCount { get; private set; }
    public DateTime? LastCheckedAt { get; private set; }
    public DateTime? LatestDetectionAt { get; private set; }

    public static MonitoredEmail Create(Guid userId, Guid domainId, string email)
        => new() { UserId = userId, DomainId = domainId, EmailAddress = email };

    public bool UpdateBreachStatus(int newBreachCount)
    {
        bool changed = newBreachCount > BreachCount;
        IsBreached = newBreachCount > 0;
        BreachCount = newBreachCount;
        LastCheckedAt = DateTime.UtcNow;
        if (changed) LatestDetectionAt = DateTime.UtcNow;
        Touch();
        return changed;
    }
}