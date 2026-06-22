using Domain.Enums;
using Domain.Common;
using MediatR;
using Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Application.Features.Repository.DTOs;

namespace Application.Features.Repository;

public record GetRepositoriesQuery(
    string? Search,
    RepositoryStatus? Status,
    string SortBy = "created_at",
    string Order = "asc",
    int Page = 1,
    int PageSize = 20) : IRequest<Result<PagedResult<RepoSummary>>>;

public record RepoFilter(
    Guid UserId,
    string? Search,
    RepositoryStatus? Status,
    string? SortBy,
    string? Order,
    int Page,
    int PageSize);

public class GetRepositoriesHandler(
    IHttpContextAccessor _http,
    IMonitoredRepoRepository repos,
    ICurrentUser currentUser)
    : IRequestHandler<GetRepositoriesQuery, Result<PagedResult<RepoSummary>>>
{
    public async Task<Result<PagedResult<RepoSummary>>> Handle(GetRepositoriesQuery query, CancellationToken ct)
    {
        var filter = new RepoFilter(
            UserId: currentUser.UserId,
            Search: query.Search?.Trim().ToLowerInvariant(),
            Status: query.Status,
            SortBy: query.SortBy.ToLowerInvariant(),
            Order: query.Order.ToLowerInvariant(),
            Page: Math.Max(query.Page, 1),  
            PageSize: Math.Clamp(query.PageSize, 1, 50));  

        var (items, totalCount) = await repos.GetPaged(filter, ct);

        var http = _http.HttpContext!;
        var basePath = http.Request.Path;
        var queryString = http.Request.QueryString.ToString();

        var summaries = items.Select(d =>
        {
            var latestScan = d.Scans
                .Where(s => s.Status == ScanStatus.Completed)
                .OrderByDescending(s => s.CompletedAt)
                .FirstOrDefault();

            return new RepoSummary(
                d.Id,
                d.FullName,
                d.CloneUrl,
                d.DefaultBranch,
                d.IsPrivate,
                d.Status,
                d.CreatedAt,
                d.UpdatedAt,
                latestScan?.CompletedAt);
        }).ToList();

        return Result<PagedResult<RepoSummary>>.Success(
            PagedResult<RepoSummary>.From(
                summaries,
                totalCount,
                filter.Page,
                filter.PageSize,
                basePath!,
                queryString));
    }
    
}
