using Application.Features.Dashboard.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Dashboard;

public record GetDashboardAttentionQuery
    : IRequest<Result<IReadOnlyList<AttentionItemDto>>>;

public class GetDashboardAttentionHandler(
    IVulnWatchDbContext db,
    ICurrentUser currentUser)
    : IRequestHandler<GetDashboardAttentionQuery,
                      Result<IReadOnlyList<AttentionItemDto>>>
{
    public async Task<Result<IReadOnlyList<AttentionItemDto>>> Handle(
        GetDashboardAttentionQuery _, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var today  = DateTime.UtcNow.Date;

        var domains = await db.Domains
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

                CriticalCount = d.Scans
                    .Where(s => s.Status == ScanStatus.Completed)
                    .OrderByDescending(s => s.CompletedAt)
                    .Take(1)
                    .SelectMany(s => s.Findings)
                    .Count(f => f.Status == FindingStatus.Open
                             && f.Severity == FindingSeverity.Critical),

                SslDays = d.SslCertExpiry == null
                    ? (int?)null
                    : (int)(d.SslCertExpiry.Value.UtcDateTime.Date - today)
                           .TotalDays
            })
            .ToListAsync(ct);

        var items = new List<AttentionItemDto>();

        foreach (var d in domains)
        {
            // Unverified domains
            if (d.VerificationStatus == VerificationStatus.Pending)
            {
                items.Add(new AttentionItemDto(
                    d.Id, d.DomainName,
                    "verify_domain",
                    $"{d.DomainName} · awaiting TXT record verification",
                    "info", 40));
                continue; // skip other checks — domain not active yet
            }

            // Critical findings
            if (d.CriticalCount > 0)
                items.Add(new AttentionItemDto(
                    d.Id, d.DomainName,
                    "fix_findings",
                    $"{d.DomainName} · {d.CriticalCount} critical finding{(d.CriticalCount > 1 ? "s" : "")}",
                    "critical", 10));

            // SSL expiry
            if (d.SslDays.HasValue)
            {
                if (d.SslDays <= 7)
                    items.Add(new AttentionItemDto(
                        d.Id, d.DomainName,
                        "renew_ssl",
                        $"{d.DomainName} · SSL expires in {d.SslDays} days",
                        "critical", 20));

                else if (d.SslDays <= 30)
                    items.Add(new AttentionItemDto(
                        d.Id, d.DomainName,
                        "renew_ssl",
                        $"{d.DomainName} · SSL expires in {d.SslDays} days",
                        "warning", 30));
            }

            // Monitoring paused
            if (!d.IsMonitoringEnabled)
                items.Add(new AttentionItemDto(
                    d.Id, d.DomainName,
                    "resume_monitoring",
                    $"{d.DomainName} · monitoring is paused",
                    "info", 50));
        }

        return Result<IReadOnlyList<AttentionItemDto>>.Success(
            items.OrderBy(i => i.SortOrder)
                 .ThenBy(i => i.DomainName)
                 .ToList());
    }
}