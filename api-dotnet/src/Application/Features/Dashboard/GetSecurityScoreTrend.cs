using Application.Features.Dashboard.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using System.Globalization;  

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

        // Build a 7-day window, taking the avg score per day
        var result = Enumerable.Range(0, 7)
            .Select(i =>
            {
                var day = today.AddDays(-6 + i);
                var dayScans = scans
                    .Where(s => s.CompletedAt!.Value.Date == day)
                    .ToList();

                var scores = dayScans
                    .Where(s => s.SecurityScore.HasValue)
                    .Select(s => s.SecurityScore!.Value)
                    .ToList();

                var score = scores.Count > 0
                    ? (int?)Math.Round(scores.Average())
                    : null;


                return new SecurityScoreTrendDto(
                    day.ToString("ddd", CultureInfo.InvariantCulture), // "Mon", "Tue"...
                    score);
            })
            .ToList();

        return Result<IReadOnlyList<SecurityScoreTrendDto>>.Success(result);
    }
}