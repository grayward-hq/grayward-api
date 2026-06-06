using Domain.Enums;
using Domain.Common;
using MediatR;
using Application.Interfaces;
using Application.Features.Domain.DTOs;
using Microsoft.AspNetCore.Http;

namespace Application.Features.Domain;
public record GetDomainByIdQuery(Guid DomainId) : IRequest<Result<DomainSummary>>;

public class GetDomainByIdHandler(
    IDomainRepository domains,
    ICurrentUser currentUser)
    : IRequestHandler<GetDomainByIdQuery, Result<DomainSummary>>
{
    public async Task<Result<DomainSummary>> Handle(GetDomainByIdQuery query, CancellationToken ct)
    {
        var domain = await domains.FindUserDomainById(currentUser.UserId, query.DomainId, ct);

        if (domain is null)
            return Result<DomainSummary>.Failure(Error.NotFound("Domain not found."));

        var latestScan = domain.Scans
            .Where(s => s.Status == ScanStatus.Completed)
            .OrderByDescending(s => s.CompletedAt)
            .FirstOrDefault();
        
        DnsInstructions? instructions = null;

        if (domain.VerificationStatus == VerificationStatus.Pending && domain.VerificationToken is not null)
        {
            instructions =  new DnsInstructions(
            TxtRecord: $"_vulnwatch-verify",
            Value: domain.VerificationToken);
        }

        return Result<DomainSummary>.Success(new DomainSummary(
            domain.Id,
            domain.DomainName,
            domain.VerificationStatus,
            domain.CreatedAt,
            domain.UpdatedAt,
            latestScan?.CompletedAt,
            latestScan?.SecurityScore,
            instructions));
    }
}