using Domain.Enums;

namespace Application.Features.Dashboard.DTOs;

public record DashboardAlertDto(
    Guid AlertId,
    Guid? DomainId,
    string? DomainName,
    AlertType Type,
    AlertSeverity Severity,
    string Subject,
    OutboxStatus Status,
    DateTime CreatedAt
);