using Domain.Enums;

namespace Application.Features.Monitoring.DTOs;

public record MonitoringOverviewDto(
    Guid DomainId,
    string DomainName,
    VerificationStatus VerificationStatus,

    // Security health
    int? SecurityScore,
    string RiskLevel,           // "safe" | "moderate" | "at_risk" | "unscanned"
    FindingCountsDto FindingCounts,

    // SSL
    SslStatusDto Ssl,

    // Domain ownership
    OwnershipStatusDto Ownership,

    // Latest scan
    LatestScanDto? LatestScan,

    // Active alerts (undelivered, for this domain)
    IReadOnlyList<AlertSummaryDto> RecentAlerts,

    // Scan timeline (last 5 events)
    IReadOnlyList<ScanTimelineItemDto> Timeline
);

public record FindingCountsDto(
    int Critical,
    int High,
    int Medium,
    int Low
);

public record SslStatusDto(
    bool HasCertificate,
    DateTimeOffset? ExpiresAt,
    int? DaysRemaining,
    SslSeverity Severity          // Safe | Warning | Urgent | Critical | Expired
);


public record OwnershipStatusDto(
    bool IsVerified,
    DateTime? LastCheckedAt,
    bool TokenExpiringSoon        // within 30 days, expandable later
);

public record LatestScanDto(
    Guid ScanId,
    ScanStatus Status,
    ScanCoverage Coverage,
    DateTime? CompletedAt,
    int? SecurityScore
);

public record AlertSummaryDto(
    Guid AlertId,
    AlertType Type,
    AlertSeverity Severity,
    string Subject,
    DateTime CreatedAt
);

public record ScanTimelineItemDto(
    string EventType,             // "scan_completed" | "scan_started" | "risk_increased" | "ssl_alert"
    string Description,
    DateTime OccurredAt,
    string Severity               // "info" | "warning" | "error" | "success"
);