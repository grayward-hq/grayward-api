using Application.Features.Profile.DTOs;
using Application.Features.Repository.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using MediatR;

namespace Application.Features.Repository;

public record GetRepoDetailQuery(Guid RepositoryId, int TrendDays = 30)
    : IRequest<Result<RepoDetailDto>>;

public class GetRepoDetailHandler(
    IMonitoredRepoRepository repos,
    IScanRepository scans,
    IFindingRepository findings,
    ICurrentUser currentUser)
    : IRequestHandler<GetRepoDetailQuery, Result<RepoDetailDto>>
{
    public async Task<Result<RepoDetailDto>> Handle(GetRepoDetailQuery q, CancellationToken ct)
    {
        var userId = currentUser.UserId;

        var repo = await repos.GetUserRepoByRepoId(userId, q.RepositoryId, ct);
        if (repo is null)
            return Result<RepoDetailDto>.Failure(Error.NotFound("Repository not found."));

        var latestScan = await scans.FindLatestForRepository(q.RepositoryId, ct);

        var latestCompleted = await scans.FindLatestCompletedForRepository(q.RepositoryId, ct);

        var since = DateTime.UtcNow.Date.AddDays(-q.TrendDays + 1);

        List<Finding> vulns = [];
        List<SeverityCountDto> openBySeverity = [];
        List<TrendPointDto> trend;

        if (latestCompleted is null)
        {
            trend = BuildTrend([], since, q.TrendDays);
        }
        else
        {
            vulns = await findings.GetByScanId(latestCompleted.Id, ct);

            openBySeverity = vulns
                .GroupBy(v => v.Severity)
                .Select(g => new SeverityCountDto(g.Key, g.Count()))
                .OrderByDescending(s => s.Severity)
                .ToList();

            var trendRows = await findings.GetTrendRowsByRepository(latestCompleted.Id, since, ct);
            trend = BuildTrend(trendRows.Select(r => (r.Severity, r.Day)), since, q.TrendDays);
        }

        var settingsDto = repo.Settings is null
            ? RepoSettingsDto_Default()
            : ToDto(repo.Settings);

        return Result<RepoDetailDto>.Success(new RepoDetailDto(
            repo.Id, repo.FullName, repo.CloneUrl, repo.DefaultBranch, repo.IsPrivate,
            settingsDto,
            latestScan?.Status,               
            latestCompleted?.CompletedAt,
            openBySeverity, vulns, trend));
    }

    private static RepoSettingsDto ToDto(RepositorySetting s) => new(
        s.PeriodicScanEnabled, s.PeriodicScanFrequency,
        s.EventScanEnabled, s.Triggers, s.AlertChannels,
        s.NextScanDueAt, s.LastScanAt,
        s.Version.ToString());

    private static RepoSettingsDto RepoSettingsDto_Default() => new(
        false, ScanFrequency.Daily, false, RepositoryEventTrigger.None,
        AlertChannel.Email, null, null, string.Empty);

    // zero-fills every day in the window so the chart has no gaps
    private static List<TrendPointDto> BuildTrend(
        IEnumerable<(FindingSeverity Severity, DateTime Day)> rows, DateTime since, int days)
    {
        var byDay = rows
            .GroupBy(r => r.Day)
            .ToDictionary(g => g.Key, g => g.ToList());

        var points = new List<TrendPointDto>(days);
        for (var i = 0; i < days; i++)
        {
            var day = since.AddDays(i);
            byDay.TryGetValue(day, out var dayRows);
            points.Add(new TrendPointDto(
                day,
                dayRows?.Count(r => r.Severity == FindingSeverity.Critical) ?? 0,
                dayRows?.Count(r => r.Severity == FindingSeverity.High)     ?? 0,
                dayRows?.Count(r => r.Severity == FindingSeverity.Medium)   ?? 0,
                dayRows?.Count(r => r.Severity == FindingSeverity.Low)      ?? 0));
        }
        return points;
    }
}