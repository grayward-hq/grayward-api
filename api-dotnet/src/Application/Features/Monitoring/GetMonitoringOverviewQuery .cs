using Application.Features.Monitoring.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using Domain.Entities;
using MediatR;

namespace Application.Features.Monitoring;

public record GetMonitoringOverviewQuery(Guid DomainId)
    : IRequest<Result<MonitoringOverviewDto>>;

public class GetMonitoringOverviewHandler(
    IDomainRepository domains,
    IScanRepository scans,
    IAlertRepository alerts,
    ICurrentUser currentUser)
    : IRequestHandler<GetMonitoringOverviewQuery, Result<MonitoringOverviewDto>>
{
    public async Task<Result<MonitoringOverviewDto>> Handle(
        GetMonitoringOverviewQuery query,
        CancellationToken ct)
    {
        // Ownership check — same pattern as GetDomainByIdHandler
        var domain = await domains.FindUserDomainById(
            currentUser.UserId, query.DomainId, ct);

        if (domain is null)
            return Result<MonitoringOverviewDto>.Failure(
                Error.NotFound("Domain not found."));

        // Run the three data fetches in sequence — no dependencies between them
        var (latestScan, recentAlerts) = await FetchDataSequentially(
            query.DomainId, currentUser.UserId, ct);

        var findings = latestScan is not null
            ? await scans.FindByIdWithFindings(latestScan.Id, ct)
            : null;

        var openFindings = findings?.Findings
            .Where(f => f.Status == FindingStatus.Open)
            .ToList() ?? [];

        return Result<MonitoringOverviewDto>.Success(new MonitoringOverviewDto(
            DomainId:          domain.Id,
            DomainName:        domain.DomainName,
            VerificationStatus: domain.VerificationStatus,
            SecurityScore:     latestScan?.SecurityScore,
            RiskLevel:         ClassifyRisk(latestScan?.SecurityScore),
            FindingCounts:     BuildFindingCounts(openFindings),
            Ssl:               BuildSslStatus(domain.SslCertExpiry),
            Ownership:         BuildOwnershipStatus(domain),
            LatestScan:        latestScan is null ? null : new LatestScanDto(
                                   latestScan.Id,
                                   latestScan.Status,
                                   latestScan.Coverage,
                                   latestScan.CompletedAt,
                                   latestScan.SecurityScore),
            RecentAlerts:      recentAlerts,
            Timeline:          BuildTimeline(latestScan, domain.SslCertExpiry)
        ));
    }

    // ── Sequential fetch ────────────────────────────────────────────────────────

    private async Task<(Scan? latestScan, IReadOnlyList<AlertSummaryDto> alerts)>
    FetchDataSequentially(Guid domainId, Guid userId, CancellationToken ct)
    {
        var latestScan = await scans.FindLatestCompletedByDomain(domainId, ct);

        var recentAlerts = await alerts.GetRecentByDomain(domainId, limit: 5, ct);

        var alertDtos = recentAlerts
            .Select(a => new AlertSummaryDto(a.Id, a.Type, a.Severity, a.Subject, a.CreatedAt))
            .ToList();

        return (latestScan, alertDtos);
    }

    // ── Builders ──────────────────────────────────────────────────────────────

    private static FindingCountsDto BuildFindingCounts(
        List<Finding> open) => new(
            Critical: open.Count(f => f.Severity == FindingSeverity.Critical),
            High:     open.Count(f => f.Severity == FindingSeverity.High),
            Medium:   open.Count(f => f.Severity == FindingSeverity.Medium),
            Low:      open.Count(f => f.Severity == FindingSeverity.Low));

    private static SslStatusDto BuildSslStatus(DateTimeOffset? expiry)
    {
        if (expiry is null)
            return new SslStatusDto(false, null, null, SslSeverity.Unknown);

        var daysRemaining = (expiry.Value.UtcDateTime.Date - DateTime.UtcNow.Date).Days;

        var severity = daysRemaining switch
        {
            <= 0  => SslSeverity.Expired,
            <= 7  => SslSeverity.Critical,
            <= 15 => SslSeverity.Urgent,
            <= 30 => SslSeverity.Warning,
            _     => SslSeverity.Safe
        };

        return new SslStatusDto(true, expiry, daysRemaining, severity);
    }

    private static OwnershipStatusDto BuildOwnershipStatus(
        ScannedDomain domain) => new(
            IsVerified:        domain.VerificationStatus == VerificationStatus.Verified,
            LastCheckedAt:     domain.UpdatedAt,
            TokenExpiringSoon: false   // placeholder — extend when DomainMonitoringSettings exists
        );

    private static IReadOnlyList<ScanTimelineItemDto> BuildTimeline(
        Scan? scan,
        DateTimeOffset? sslExpiry)
    {
        var items = new List<ScanTimelineItemDto>();

        if (scan is null) return items;

        if (scan.CompletedAt.HasValue)
            items.Add(new ScanTimelineItemDto(
                "scan_completed",
                $"Scan completed · Score {scan.SecurityScore ?? 0}",
                scan.CompletedAt.Value,
                scan.SecurityScore < 60 ? "warning" : "success"));

        if (scan.StartedAt.HasValue)
            items.Add(new ScanTimelineItemDto(
                "scan_started",
                $"Scan started · {scan.Coverage} coverage",
                scan.StartedAt.Value,
                "info"));

        if (sslExpiry.HasValue)
        {
            var days = (sslExpiry.Value.UtcDateTime.Date - DateTime.UtcNow.Date).Days;
            if (days <= 30)
                items.Add(new ScanTimelineItemDto(
                    "ssl_alert",
                    $"SSL expiry alert · {days} days remaining",
                    scan.CompletedAt ?? scan.CreatedAt,
                    days <= 7 ? "error" : "warning"));
        }

        return items
            .OrderByDescending(i => i.OccurredAt)
            .Take(5)
            .ToList();
    }

    private static string ClassifyRisk(int? score) => score switch
    {
        >= 80 => "safe",
        >= 60 => "moderate",
        null  => "unscanned",
        _     => "at_risk"
    };
}