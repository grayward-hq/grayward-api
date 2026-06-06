using Domain.Entities;

namespace Domain.Events;

public record CredentialBreachEvent(
    ScannedDomain Domain,
    MonitoredEmail Email,
    List<string> BreachNames) : IDomainEvent
{
    public Guid UserId    => Domain.UserId;
    public Guid DomainId  => Domain.Id;
    public string DomainName => Domain.DomainName;
}