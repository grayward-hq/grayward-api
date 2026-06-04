using Application.Features.Dashboard.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Dashboard;

public record GetDashboardSummaryQuery : IRequest<Result<DashboardSummaryDto>>;

public class GetDashboardSummaryHandler(
    IVulnWatchDbContext db,
    ICurrentUser currentUser)
    : IRequestHandler<GetDashboardSummaryQuery, Result<DashboardSummaryDto>>
{
    public async Task<Result<DashboardSummaryDto>> Handle(
        GetDashboardSummaryQuery _, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var today = DateTime.UtcNow.Date;

        // Single projection — all aggregation in one round trip
        var domainData = await db.Domains
            .Where(d => d.UserId == userId)
            .Select(d => new
            {
                d.Id,
                d.DomainName,
                d.VerificationStatus,
                d.SslCertExpiry,

                IsMonitoringEnabled = db.DomainSettings
                    .Where(s => s.DomainId == d.Id)
                    .Select(s => (bool?)s.MonitoringEnabled)
                    .FirstOrDefault() ?? false,

                // Single subquery — replaces four separate LatestScore /
                // LatestScanId / LatestScanAt / CriticalCount+OpenCount subqueries
                LatestScan = d.Scans
                    .Where(s => s.Status == ScanStatus.Completed)
                    .OrderByDescending(s => s.CompletedAt)
                    .Select(s => new
                    {
                        s.Id,
                        s.SecurityScore,
                        s.CompletedAt,
                        CriticalCount = s.Findings
                            .Count(f => f.Status == FindingStatus.Open
                                     && f.Severity == FindingSeverity.Critical),
                        OpenCount = s.Findings
                            .Count(f => f.Status == FindingStatus.Open)
                    })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        // Active SSL expiry alerts — separate query, small result set
        var sslAlertCount = await db.Alerts
            .Where(a => a.UserId == userId
                     && a.Type == AlertType.SslExpiry
                     && a.Status == OutboxStatus.Pending)
            .CountAsync(ct);

        var scores = domainData
            .Where(d => d.LatestScan is not null)
            .Select(d => d.LatestScan!.SecurityScore)
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .ToList();

        var avgScore = scores.Count > 0
            ? (int)Math.Round(scores.Average())
            : (int?)null;

        var totalCritical = domainData.Sum(d => d.LatestScan?.CriticalCount ?? 0);

        var overallPosture = ClassifyPosture(avgScore, totalCritical);

        // Most urgent SSL — domain with fewest days left at or above zero
        var mostUrgentSsl = domainData
            .Where(d => d.SslCertExpiry.HasValue)
            .Select(d => new
            {
                d.Id,
                d.DomainName,
                Days = (d.SslCertExpiry!.Value.UtcDateTime.Date - today).Days
            })
            .Where(d => d.Days >= 0)
            .OrderBy(d => d.Days)
            .Select(d => new SslUrgentDto(
                d.Id,
                d.DomainName,
                d.Days,
                ClassifySslSeverity(d.Days)))
            .FirstOrDefault();

        // Most recent scan across all domains
        var mostRecent = domainData
            .Where(d => d.LatestScan is not null && d.LatestScan.CompletedAt.HasValue)
            .OrderByDescending(d => d.LatestScan!.CompletedAt)
            .Select(d => new LastScanDto(
                d.LatestScan!.Id,
                d.Id,
                d.DomainName,
                d.LatestScan.SecurityScore,
                d.LatestScan.CompletedAt!.Value))
            .FirstOrDefault();

        return Result<DashboardSummaryDto>.Success(new DashboardSummaryDto(
            TotalDomains: domainData.Count,
            VerifiedDomains: domainData.Count(d => d.VerificationStatus == VerificationStatus.Verified),
            MonitoringActiveDomains: domainData.Count(d => d.IsMonitoringEnabled),
            OverallPosture: overallPosture,
            AvgSecurityScore: avgScore,
            TotalCriticalFindings: totalCritical,
            TotalOpenFindings: domainData.Sum(d => d.LatestScan?.OpenCount ?? 0),
            SslAlertsActive: sslAlertCount,
            MostUrgentSsl: mostUrgentSsl,
            MostRecentScan: mostRecent));
    }

    private static string ClassifyPosture(int? avg, int totalCritical) =>
        totalCritical > 0 ? "at_risk" :
        avg is null ? "unscanned" :
        avg >= 80 ? "safe" :
        avg >= 60 ? "moderate" :
                            "at_risk";

    private static SslSeverity ClassifySslSeverity(int days) => days switch
    {
        <= 0 => SslSeverity.Expired,
        <= 7 => SslSeverity.Critical,
        <= 15 => SslSeverity.Urgent,
        <= 30 => SslSeverity.Warning,
        _ => SslSeverity.Safe
    };
}