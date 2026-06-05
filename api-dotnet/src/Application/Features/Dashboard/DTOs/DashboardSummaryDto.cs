using Domain.Enums;

namespace Application.Features.Dashboard.DTOs;

public record DashboardSummaryDto(
    
    int TotalDomains,
    int VerifiedDomains,
    int MonitoringActiveDomains,

    // Posture cards
    string OverallPosture,        // "safe" | "moderate" | "at_risk" | "unscanned"
    int? AvgSecurityScore,        // null if no scans exist yet
    int TotalCriticalFindings,
    int TotalOpenFindings,

    // SSL card
    int SslAlertsActive,          // pending SSL expiry alerts
    SslUrgentDto? MostUrgentSsl,  // the domain with fewest days remaining

    // Last scan card
    LastScanDto? MostRecentScan,

    SeverityBreakdownDto SeverityBreakdown 
);

public record SeverityBreakdownDto(
    int Critical,
    int High,
    int Medium,
    int Low
);

public record SslUrgentDto(
    Guid DomainId,
    string DomainName,
    int DaysRemaining,
    SslSeverity Severity
);

public record LastScanDto(
    Guid ScanId,
    Guid DomainId,
    string DomainName,
    int? SecurityScore,
    DateTime CompletedAt
);