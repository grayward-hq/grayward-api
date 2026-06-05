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