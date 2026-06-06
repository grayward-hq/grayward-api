using Domain.Common;
using Domain.Entities;

namespace Application.Features.BreachMonitoring.DTOs;

public record MonitoredEmailDto(
    Guid Id,
    string EmailAddress,
    bool IsBreached,
    int BreachCount,
    DateTime? LastCheckedAt,
    DateTime? LatestDetectionAt,
    DateTime CreatedAt)
{
    public static MonitoredEmailDto From(MonitoredEmail e) => new(
        e.Id,
        e.EmailAddress,
        e.IsBreached,
        e.BreachCount,
        e.LastCheckedAt,
        e.LatestDetectionAt,
        e.CreatedAt);
}

public record MonitoredEmailSummary(int Total, int Breached, int NotBreached);

public record MonitoredEmailsPagedDto(
    int TotalEmails,
    int BreachedCount,
    int NotBreachedCount,
    PagedResult<MonitoredEmailDto> Emails);