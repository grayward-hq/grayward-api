using Application.Features.Dashboard.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using FluentValidation;

namespace Application.Features.Dashboard;

public class GetDashboardDomainsQueryValidator
    : AbstractValidator<GetDashboardDomainsQuery>
{
    public GetDashboardDomainsQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThan(0)
            .WithMessage("Page must be greater than 0.");

        RuleFor(x => x.PageSize)
            .GreaterThan(0)
            .WithMessage("PageSize must be greater than 0.")
            .LessThanOrEqualTo(50)
            .WithMessage("PageSize cannot exceed 50.");
    }
}

public record GetDashboardDomainsQuery(
    int Page = 1,
    int PageSize = 10)
    : IRequest<Result<PagedResult<DashboardDomainRowDto>>>;

public class GetDashboardDomainsHandler(
    IVulnWatchDbContext db,
    ICurrentUser currentUser)
    : IRequestHandler<GetDashboardDomainsQuery,
                      Result<PagedResult<DashboardDomainRowDto>>>
{
    public async Task<Result<PagedResult<DashboardDomainRowDto>>> Handle(
        GetDashboardDomainsQuery query, CancellationToken ct)
    {
        var userId   = currentUser.UserId;
        var today    = DateTime.UtcNow.Date;
        var pageSize = query.PageSize;

        var rows = await db.Domains
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.Scans
                .Where(s => s.Status == ScanStatus.Completed)
                .OrderByDescending(s => s.CompletedAt)
                .Select(s => (int?)s.SecurityScore)
                .FirstOrDefault() ?? 101)   // unscanned sorts last
            .Select(d => new
            {
                d.Id,
                d.DomainName,

                IsMonitoringEnabled = db.DomainSettings
                    .Where(s => s.DomainId == d.Id)
                    .Select(s => (bool?)s.MonitoringEnabled)
                    .FirstOrDefault() ?? false,

                // Single subquery — finds the latest completed scan once,
                // then projects all needed properties from it.
                LatestScan = d.Scans
                    .Where(s => s.Status == ScanStatus.Completed)
                    .OrderByDescending(s => s.CompletedAt)
                    .Select(s => new
                    {
                        s.SecurityScore,
                        s.CompletedAt,
                        CriticalCount = s.Findings
                            .Count(f => f.Status == FindingStatus.Open
                                     && f.Severity == FindingSeverity.Critical),
                        OpenCount = s.Findings
                            .Count(f => f.Status == FindingStatus.Open)
                    })
                    .FirstOrDefault(),

                SslDays = d.SslCertExpiry == null
                    ? (int?)null
                    : (int)(d.SslCertExpiry.Value.UtcDateTime.Date - today).TotalDays
            })
            .ToListAsync(ct);

        var totalCount = rows.Count;

        var dtos = rows
            .Skip((query.Page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new DashboardDomainRowDto(
                DomainId:          r.Id,
                DomainName:        r.DomainName,
                MonitoringEnabled: r.IsMonitoringEnabled,
                SecurityScore:     r.LatestScan?.SecurityScore,
                RiskLevel:         ClassifyRisk(r.LatestScan?.SecurityScore),
                SslDaysRemaining:  r.SslDays,
                SslSeverity:       ClassifySsl(r.SslDays),
                CriticalFindings:  r.LatestScan?.CriticalCount ?? 0,
                TotalOpenFindings: r.LatestScan?.OpenCount ?? 0,
                LastScannedAt:     r.LatestScan?.CompletedAt))
            .ToList();

        return Result<PagedResult<DashboardDomainRowDto>>.Success(
            PagedResult<DashboardDomainRowDto>.From(
                dtos, totalCount, query.Page, pageSize,
                "/api/dashboard/domains", string.Empty));
    }

    private static string ClassifyRisk(int? score) => score switch
    {
        >= 80 => "safe",
        >= 60 => "moderate",
        null  => "unscanned",
        _     => "at_risk"
    };

    private static SslSeverity ClassifySsl(int? days) => days switch
    {
        null  => SslSeverity.Unknown,
        <= 0  => SslSeverity.Expired,
        <= 7  => SslSeverity.Critical,
        <= 15 => SslSeverity.Urgent,
        <= 30 => SslSeverity.Warning,
        _     => SslSeverity.Safe
    };
}