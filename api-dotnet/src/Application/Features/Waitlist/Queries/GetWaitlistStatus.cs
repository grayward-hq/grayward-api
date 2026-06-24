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
        var totalCount = await _waitlistRepo.GetTotalCount(ct);
        var normalizedEmail = query.Email.ToLowerInvariant();

        _logger.LogDebug("Waitlist status query masked for email: {email}", query.Email);

        return Result<WaitlistStatusResponse>.Success(
            new WaitlistStatusResponse(
                normalizedEmail,
                Position: 0,
                totalCount,
                WaitlistStatus.Pending,
                EmailConfirmed: false,
                JoinedAt: DateTime.UtcNow));
    }
}
