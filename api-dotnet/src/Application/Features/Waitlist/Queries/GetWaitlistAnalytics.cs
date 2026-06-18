using Application.Features.Waitlist.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Features.Waitlist.Queries;

public record GetWaitlistAnalyticsQuery : IRequest<Result<WaitlistAnalyticsDto>>;

public class GetWaitlistAnalyticsHandler : IRequestHandler<GetWaitlistAnalyticsQuery, Result<WaitlistAnalyticsDto>>
{
    private readonly IWaitlistRepository _waitlistRepo;
    private readonly ILogger<GetWaitlistAnalyticsHandler> _logger;

    public GetWaitlistAnalyticsHandler(
        IWaitlistRepository waitlistRepo,
        ILogger<GetWaitlistAnalyticsHandler> logger)
    {
        _waitlistRepo = waitlistRepo;
        _logger = logger;
    }

    public async Task<Result<WaitlistAnalyticsDto>> Handle(GetWaitlistAnalyticsQuery query, CancellationToken ct)
    {
        var totalOnWaitlist = await _waitlistRepo.GetTotalCount(ct);
        var pendingCount = await _waitlistRepo.CountByStatus(WaitlistStatus.Pending, ct);
        var emailConfirmedCount = await _waitlistRepo.CountByStatus(WaitlistStatus.EmailConfirmed, ct);
        var promotedCount = await _waitlistRepo.CountByStatus(WaitlistStatus.Promoted, ct);
        var cancelledCount = await _waitlistRepo.CountByStatus(WaitlistStatus.Cancelled, ct);

        var promotionRate = totalOnWaitlist > 0 ? (decimal)promotedCount / totalOnWaitlist * 100 : 0;
        var cancellationRate = totalOnWaitlist > 0 ? (decimal)cancelledCount / totalOnWaitlist * 100 : 0;

        var averageDaysToPromotion = await _waitlistRepo.GetAverageDaysToPromotion(ct);

        var topCompanies = await _waitlistRepo.GetTopCompanies(ct, limit: 10);

        _logger.LogInformation("Waitlist analytics retrieved: {total} total, {promoted} promoted, {cancelled} cancelled", 
            totalOnWaitlist, promotedCount, cancelledCount);

        return Result<WaitlistAnalyticsDto>.Success(
            new WaitlistAnalyticsDto(
                totalOnWaitlist,
                pendingCount,
                emailConfirmedCount,
                promotedCount,
                cancelledCount,
                Math.Round(promotionRate, 2),
                Math.Round(cancellationRate, 2),
                averageDaysToPromotion,
                topCompanies));
    }
}
