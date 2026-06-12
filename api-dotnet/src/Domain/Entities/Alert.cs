using Domain.Enums;

namespace Domain.Entities;

public class Alert : EntityBase
{
    public Guid UserId { get; private set; }
    public Guid? DomainId { get; private set; }
    public AlertType Type { get; private set; }
    public AlertChannel Channel { get; private set; }
    public AlertSeverity Severity { get; private set; }
    public string DeduplicationKey { get; init; } = string.Empty;
    public string Subject { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public string? Summary { get; private set; }
    public OutboxStatus Status { get; private set; }
    public int NumRetries { get; private set; }
    public DateTime? SentAt { get; private set; }
    public string? ErrorMessage { get; private set; }

    private Alert() { }

    public static Alert Create(
          Guid userId,
          AlertType type,
          AlertChannel channel,
          AlertSeverity severity,
          string deduplicationKey,
          string subject,
          string body,
          Guid? domainId = null,
          string? summary = null)
    {
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject cannot be null or empty.", nameof(subject));
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Body cannot be null or empty.", nameof(body));

        return new()
        {
            UserId = userId,
            DomainId = domainId,
            Type = type,
            Channel = channel,
            Severity = severity,
            DeduplicationKey = deduplicationKey,
            Subject = subject,
            Body = body,
            Summary = summary,
            Status = OutboxStatus.Pending,
        };
    }

    public void MarkSent()
    {
        Status = OutboxStatus.Delivered;
        SentAt = DateTime.UtcNow;
        Touch();
    }

    public void MarkFailed(string error)
    {
        NumRetries++;
        ErrorMessage = error;
        if (NumRetries >= 3)
            Status = OutboxStatus.DeadLetter;
        Touch();
    }
}