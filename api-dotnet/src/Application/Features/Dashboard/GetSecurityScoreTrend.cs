using Application.Features.Dashboard.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using FluentValidation;

namespace Application.Features.Dashboard;

public record GetSecurityScoreTrendQuery : IRequest<Result<IReadOnlyList<SecurityScoreTrendDto>>>;

public record SecurityScoreTrendDto(string Day, int? Score); 

public class GetSecurityScoreTrendHandler(
    // IVulnWatchDbContext db,
    IScanRepository scanRepo,
    ICurrentUser currentUser)
    : IRequestHandler<GetSecurityScoreTrendQuery, Result<IReadOnlyList<SecurityScoreTrendDto>>>
{
    public async Task<Result<IReadOnlyList<SecurityScoreTrendDto>>> Handle(
        GetSecurityScoreTrendQuery _, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var today = DateTime.UtcNow.Date;
        var sevenDaysAgo = today.AddDays(-6);

        var scans = await scanRepo.GetRecentCompletedScans(userId, sevenDaysAgo, ct);
        // var scans = await db.Scans
        //     .Where(s => s.UserId == userId
        //              && s.Status == ScanStatus.Completed
        //              && s.CompletedAt >= sevenDaysAgo)
        //     .Select(s => new { s.CompletedAt, s.SecurityScore })
        //     .ToListAsync(ct);

        // Build a 7-day window, taking the avg score per day
        var result = Enumerable.Range(0, 7)
            .Select(i =>
            {
                var day = today.AddDays(-6 + i);
                var dayScans = scans
                    .Where(s => s.CompletedAt!.Value.Date == day)
                    .ToList();

                var score = dayScans.Count > 0
                    ? (int?)Math.Round(dayScans.Average(s => s.SecurityScore ?? 0))
                    : null;

                return new SecurityScoreTrendDto(
                    day.ToString("ddd"), // "Mon", "Tue"...
                    score);
            })
            .ToList();

        return Result<IReadOnlyList<SecurityScoreTrendDto>>.Success(result);
    }
}