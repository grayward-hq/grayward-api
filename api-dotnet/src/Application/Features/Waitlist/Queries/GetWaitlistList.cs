using Application.Features.Waitlist.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Application.Features.Waitlist.Queries;

public record GetWaitlistListQuery(
    WaitlistStatus? Status = null,
    int Page = 1,
    int PageSize = 50,
    string? SearchEmail = null,
    string SortBy = "position",
    string SortOrder = "asc")
    : IRequest<Result<PagedResult<WaitlistListItemDto>>>;

public class GetWaitlistListHandler : IRequestHandler<GetWaitlistListQuery, Result<PagedResult<WaitlistListItemDto>>>
{
    private readonly IWaitlistRepository _waitlistRepo;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<GetWaitlistListHandler> _logger;

    public GetWaitlistListHandler(
        IWaitlistRepository waitlistRepo,
        IHttpContextAccessor http,
        ILogger<GetWaitlistListHandler> logger)
    {
        _waitlistRepo = waitlistRepo;
        _http = http;
        _logger = logger;
    }

    public async Task<Result<PagedResult<WaitlistListItemDto>>> Handle(GetWaitlistListQuery query, CancellationToken ct)
    {
        if (query.Page < 1 || query.PageSize < 1 || query.PageSize > 500)
        {
            return Result<PagedResult<WaitlistListItemDto>>.Failure(
                Error.Validation("Invalid pagination parameters"));
        }

        var (items, totalCount) = await _waitlistRepo.GetPaged(
            query.Status,
            query.Page,
            query.PageSize,
            query.SearchEmail,
            query.SortBy,
            query.SortOrder,
            ct);

        var dtos = items.Select(w => new WaitlistListItemDto(
            w.Id,
            w.Email,
            w.CompanyName,
            w.Position,
            w.Status,
            w.EmailConfirmed,
            w.CreatedAt,
            w.EmailConfirmedAt,
            w.PromotedAt,
            w.Comments,
            w.Notes,
            w.ReferralCode,
            w.ReferredByWaitlistId,
            w.ReferralCount,
            w.LastReferralAt)).ToList();

        var http = _http.HttpContext;
        var pagedResult = PagedResult<WaitlistListItemDto>.From(
            dtos,
            totalCount,
            query.Page,
            query.PageSize,
            http?.Request.Path.Value ?? "/api/waitlist/admin/list",
            http?.Request.QueryString.ToString());

        _logger.LogInformation("Fetched waitlist page {page}, size {pageSize}, total {total}", 
            query.Page, query.PageSize, totalCount);

        return Result<PagedResult<WaitlistListItemDto>>.Success(pagedResult);
    }
}
