using Domain.Enums;

namespace Application.Features.Dashboard.DTOs;

public record DashboardDomainRowDto(
    Guid DomainId,
    string DomainName,
    bool MonitoringEnabled,
    int? SecurityScore,
    string RiskLevel,
    int? SslDaysRemaining,
    SslSeverity SslSeverity,
    int CriticalFindings,
    int TotalOpenFindings,
    DateTime? LastScannedAt
);