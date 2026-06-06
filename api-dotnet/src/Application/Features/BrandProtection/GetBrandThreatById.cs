using Application.Features.BrandProtection.DTOs;
using Application.Interfaces;
using Domain.Common;
using MediatR;

namespace Application.Features.BrandProtection;

public record GetBrandThreatByIdQuery(
    Guid DomainId,
    Guid ThreatId) : IRequest<Result<BrandThreatDetailDto>>;

public class GetBrandThreatByIdHandler(
    IDomainRepository domainRepo,
    IBrandThreatRepository brandThreatRepo,
    ICurrentUser currentUser)
    : IRequestHandler<GetBrandThreatByIdQuery, Result<BrandThreatDetailDto>>
{
    public async Task<Result<BrandThreatDetailDto>> Handle(
        GetBrandThreatByIdQuery query, CancellationToken ct)
    {
        var domain = await domainRepo.FindUserDomainById(
            currentUser.UserId, query.DomainId, ct);

        if (domain is null)
            return Result<BrandThreatDetailDto>.Failure(
                Error.NotFound("Domain not found."));

        var threat = await brandThreatRepo.FindByIdAndDomain(
            query.ThreatId, query.DomainId, ct);

        if (threat is null)
            return Result<BrandThreatDetailDto>.Failure(
                Error.NotFound("Brand threat not found."));

        return Result<BrandThreatDetailDto>.Success(
            BrandThreatDetailDto.From(threat, domain.DomainName));
    }
}