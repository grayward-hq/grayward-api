using Application.Features.Waitlist.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Features.Waitlist.Queries;

public record GetWaitlistStatusQuery(string Email) 
    : IRequest<Result<WaitlistStatusResponse>>;

public class GetWaitlistStatusHandler : IRequestHandler<GetWaitlistStatusQuery, Result<WaitlistStatusResponse>>
{
    private readonly IWaitlistRepository _waitlistRepo;
    private readonly ILogger<GetWaitlistStatusHandler> _logger;

    public GetWaitlistStatusHandler(
        IWaitlistRepository waitlistRepo,
        ILogger<GetWaitlistStatusHandler> logger)
    {
        _waitlistRepo = waitlistRepo;
        _logger = logger;
    }

    public async Task<Result<WaitlistStatusResponse>> Handle(GetWaitlistStatusQuery query, CancellationToken ct)
    {
        var entry = await _waitlistRepo.FindByEmail(query.Email, ct);

        if (entry is null)
        {
            _logger.LogDebug("Waitlist status requested for email not on the waitlist");
            return Result<WaitlistStatusResponse>.Failure(
                Error.NotFound("Email not found on the waitlist"));
        }

        // Position is only meaningful once confirmed; it is the live rank among active entries
        // (so cancellations shift everyone up). Unconfirmed/cancelled entries report 0.
        long livePosition = 0;
        if (entry.Status == WaitlistStatus.EmailConfirmed && entry.Position is long sequence)
        {
            livePosition = await _waitlistRepo.GetLivePosition(sequence, ct);
        }

        var totalCount = await _waitlistRepo.GetTotalCount(ct);

        return Result<WaitlistStatusResponse>.Success(
            new WaitlistStatusResponse(
                entry.Email,
                livePosition,
                totalCount,
                entry.Status,
                entry.EmailConfirmed,
                JoinedAt: entry.CreatedAt));
    }
}
