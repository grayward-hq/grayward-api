using Domain.Enums;

namespace Application.Features.Monitoring.DTOs;

public record MonitoringSettingsDto(
    Guid DomainId,
    string DomainName,
    bool MonitoringEnabled,
    ScanFrequency ScanFrequency,
    string NextScanIn,              // human-readable, built in handler
    DateTime? LastMonitoredAt,
    DateTime? NextScheduledAt,
    IReadOnlyList<int> SslAlertThresholds,
    IReadOnlyList<string> NotificationChannels
);
