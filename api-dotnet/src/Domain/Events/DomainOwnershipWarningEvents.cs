using Domain.Enums;

namespace Domain.Events;

public record DomainOwnershipWarningEvent(
    Guid DomainId,
    Guid UserId,
    string DomainName,
    DateTime FailedSince,
    OwnershipWarningStage Stage) : IDomainEvent;