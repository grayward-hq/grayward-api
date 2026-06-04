using Domain.Enums;

namespace Domain.Events;

public record ChatMessageEvent(
    Guid ChatSessionId,
    Guid ScanId,
    Guid UserId,
    ChatMessageRole Role,
    string Content,
    DateTime OccurredAt) : IDomainEvent;