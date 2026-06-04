using Domain.Enums;

namespace Application.Features.Monitoring.DTOs;

public record UpdateMonitoringSettingsRequest(
    bool MonitoringEnabled,
    ScanFrequency ScanFrequency,
    List<int> SslAlertThresholds,
    List<AlertChannel> NotificationChannels
);