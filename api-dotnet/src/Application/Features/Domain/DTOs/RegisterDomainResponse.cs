using Domain.Entities;
using Domain.Enums;

namespace Application.Features.Domain.DTOs;

public record DnsInstructions(string TxtRecord, string Value);

public record RegisterDomainResponse(Guid Id, string DomainName, string VerificationToken, VerificationStatus Status, DnsInstructions Instructions)
{
    public static RegisterDomainResponse Create(string token, ScannedDomain domain) => new(
        domain.Id,
        domain.DomainName,
        token,
        domain.VerificationStatus,
        new DnsInstructions(
            TxtRecord: $"_grayward-verify",
            Value: token));
}

