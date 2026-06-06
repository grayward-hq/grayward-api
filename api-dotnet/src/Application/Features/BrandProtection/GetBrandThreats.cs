using Application.Features.BrandProtection.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Features.BrandProtection;

public record GetBrandThreatsQuery(
    Guid DomainId,
    BrandThreatStatus? Status   = null,
    BrandThreatRiskLevel? RiskLevel = null,
    int Page     = 1,
    int PageSize = 20) : IRequest<Result<BrandThreatsPagedDto>>;

public class GetBrandThreatsHandler(
    IDomainRepository domainRepo,
    IBrandThreatRepository brandThreatRepo,
    ICurrentUser currentUser,
    IHttpContextAccessor http)
    : IRequestHandler<GetBrandThreatsQuery, Result<BrandThreatsPagedDto>>
{
    public async Task<Result<BrandThreatsPagedDto>> Handle(
        GetBrandThreatsQuery query, CancellationToken ct)
    {
        var domain = await domainRepo.FindUserDomainById(
            currentUser.UserId, query.DomainId, ct);

        if (domain is null)
            return Result<BrandThreatsPagedDto>.Failure(
                Error.NotFound("Domain not found."));

        var pageSize = Math.Min(query.PageSize, 50);

        var (items, total) = await brandThreatRepo.GetPagedByDomain(
            query.DomainId, query.Status, query.RiskLevel,
            query.Page, pageSize, ct);

        var summary = await brandThreatRepo.GetSummaryByDomain(query.DomainId, ct);

        var ctx = http.HttpContext ?? throw new InvalidOperationException("HttpContext is not available.");
        var dtos = items.Select(BrandThreatDto.From).ToList();

        var paged = PagedResult<BrandThreatDto>.From(
            dtos, total, query.Page, pageSize,
            ctx.Request.Path,
            ctx.Request.QueryString.ToString());

        return Result<BrandThreatsPagedDto>.Success(new BrandThreatsPagedDto(
            TotalThreats:  summary.Total,
            ActiveCount:   summary.Active,
            ResolvedCount: summary.Resolved,
            MonitoringCount: summary.Monitoring,
            Threats: paged));
    }
}