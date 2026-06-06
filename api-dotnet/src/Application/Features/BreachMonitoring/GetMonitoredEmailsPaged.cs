using Application.Features.BreachMonitoring.DTOs;
using Application.Interfaces;
using Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Features.BreachMonitoring;

public record GetMonitoredEmailsPagedQuery(
    Guid DomainId,
    bool? IsBreached = null,
    int Page         = 1,
    int PageSize     = 20) : IRequest<Result<MonitoredEmailsPagedDto>>;

public class GetMonitoredEmailsPagedHandler(
    IDomainRepository domainRepo,
    IMonitoredEmailRepository emailRepo,
    ICurrentUser currentUser,
    IHttpContextAccessor http)
    : IRequestHandler<GetMonitoredEmailsPagedQuery, Result<MonitoredEmailsPagedDto>>
{
    public async Task<Result<MonitoredEmailsPagedDto>> Handle(
        GetMonitoredEmailsPagedQuery query, CancellationToken ct)
    {
        var domain = await domainRepo.FindUserDomainById(
            currentUser.UserId, query.DomainId, ct);

        if (domain is null)
            return Result<MonitoredEmailsPagedDto>.Failure(
                Error.NotFound("Domain not found."));

        var pageSize = Math.Min(query.PageSize, 50);

        var (items, total) = await emailRepo.GetPagedByDomain(
            query.DomainId, query.IsBreached, query.Page, pageSize, ct);

        var summary = await emailRepo.GetSummaryByDomain(query.DomainId, ct);

        var ctx = http.HttpContext!;
        var dtos = items.Select(MonitoredEmailDto.From).ToList();

        var paged = PagedResult<MonitoredEmailDto>.From(
            dtos, total, query.Page, pageSize,
            ctx.Request.Path,
            ctx.Request.QueryString.ToString());

        return Result<MonitoredEmailsPagedDto>.Success(new MonitoredEmailsPagedDto(
            TotalEmails:     summary.Total,
            BreachedCount:   summary.Breached,
            NotBreachedCount: summary.NotBreached,
            Emails:          paged));
    }
}