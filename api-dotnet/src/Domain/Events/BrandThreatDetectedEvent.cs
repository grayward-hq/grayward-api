using Domain.Entities;

namespace Domain.Events;

public record BrandThreatDetectedEvent(
    ScannedDomain Domain,
    BrandThreat Threat) : IDomainEvent
{
    public Guid UserId    => Domain.UserId;
    public Guid DomainId  => Domain.Id;
    public string DomainName => Domain.DomainName;
}