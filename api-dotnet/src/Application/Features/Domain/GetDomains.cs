using Domain.Enums;
using Domain.Common;
using MediatR;
using Application.Interfaces;
using Application.Features.Domain.DTOs;
using Microsoft.AspNetCore.Http;

namespace Application.Features.Domain;

public record GetDomainsQuery(
    string? Search,
    VerificationStatus? Status,
    string SortBy = "created_at",
    string Order = "asc",
    int Page = 1,
    int PageSize = 20) : IRequest<Result<PagedResult<DomainSummary>>>;

public record DomainFilter(
    Guid UserId,
    string? Search,
    VerificationStatus? Status,
    string? SortBy,
    string? Order,
    int Page,
    int PageSize);

public class GetDomainsHandler(
    IHttpContextAccessor _http,
    IDomainRepository domains,
    ICurrentUser currentUser)
    : IRequestHandler<GetDomainsQuery, Result<PagedResult<DomainSummary>>>
{
    public async Task<Result<PagedResult<DomainSummary>>> Handle(GetDomainsQuery query, CancellationToken ct)
    {

        var filter = new DomainFilter(
            UserId: currentUser.UserId,
            Search: query.Search?.Trim().ToLowerInvariant(),
            Status: query.Status,
            SortBy: query.SortBy.ToLowerInvariant(),
            Order: query.Order.ToLowerInvariant(),
            Page: query.Page,
            PageSize: Math.Min(query.PageSize, 50));

        var (items, totalCount) = await domains.GetPaged(filter, ct);

        var http = _http.HttpContext!;
        var basePath = http.Request.Path;
        var queryString = http.Request.QueryString.ToString();

        var summaries = items.Select(d =>
        {
            var latestScan = d.Scans
                .Where(s => s.Status == ScanStatus.Completed)
                .OrderByDescending(s => s.CompletedAt)
                .FirstOrDefault();

            DnsInstructions? instructions = null;

            if (d.VerificationStatus == VerificationStatus.Pending && d.VerificationToken is not null)
            {
                instructions =  new DnsInstructions(
                TxtRecord: $"_vulnwatch-verify",
                Value: d.VerificationToken);
            }

            return new DomainSummary(
                d.Id,
                d.DomainName,
                d.VerificationStatus,
                d.CreatedAt,
                d.UpdatedAt,
                latestScan?.CompletedAt,
                latestScan?.SecurityScore,
                null);
        }).ToList();


        return Result<PagedResult<DomainSummary>>.Success(
            PagedResult<DomainSummary>.From(
                summaries,
                totalCount,
                filter.Page,
                filter.PageSize,
                basePath!,
                queryString));
    }
}