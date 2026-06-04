using Domain.Enums;

namespace Domain.Entities;

public class DomainSettings : EntityBase
{
    public Guid DomainId { get; private set; }
    public bool MonitoringEnabled { get; private set; }
    public ScanFrequency ScanFrequency { get; private set; }

    // SSL alert thresholds — stored as comma-separated ints,
    // projected to a list by the application layer
    private string _sslAlertThresholds = "30,14,7";
    public string SslAlertThresholds
    {
        get => _sslAlertThresholds;
        private set => _sslAlertThresholds = value;
    }

    public AlertChannel NotificationChannel { get; private set; }
    public DateTime? LastMonitoredAt { get; private set; }
    public DateTime? NextScheduledAt { get; private set; }

    public ScannedDomain Domain { get; private set; } = default!;

    private DomainSettings() { }

    public static DomainSettings CreateDefault(Guid domainId) => new()
    {
        DomainId = domainId,
        MonitoringEnabled = true,
        ScanFrequency = ScanFrequency.Daily,
        NotificationChannel = AlertChannel.Email,
        _sslAlertThresholds = "30,14,7",
        NextScheduledAt = DateTime.UtcNow.AddHours(24)
    };

    public void UpdateSettings(
        bool monitoringEnabled,
        ScanFrequency scanFrequency,
        IReadOnlyList<int> sslAlertThresholds,
        AlertChannel notificationChannels)
    {
        if (notificationChannels == AlertChannel.None)
            throw new ArgumentException(
                "At least one notification channel must be selected.");

        var validMask = AlertChannel.Email | AlertChannel.Slack | AlertChannel.Push;
        if ((notificationChannels & ~validMask) != 0)
            throw new ArgumentException("Invalid notification channel value.");

        if (sslAlertThresholds.Count == 0)
            throw new ArgumentException(
                "At least one SSL alert threshold is required.");

        if (sslAlertThresholds.Any(t => t is < 1 or > 365))
            throw new ArgumentException(
                "SSL alert thresholds must be between 1 and 365 days.");

        var frequencyChanged = ScanFrequency != scanFrequency;
        var wasDisabled = !MonitoringEnabled && monitoringEnabled;

        MonitoringEnabled = monitoringEnabled;
        ScanFrequency = scanFrequency;
        NotificationChannel = notificationChannels;

        var normalizedThresholds = sslAlertThresholds
            .Distinct()
            .OrderDescending()
            .ToArray();
        var serializedThresholds = string.Join(",", normalizedThresholds);
        if (serializedThresholds.Length > 50)
            throw new ArgumentException("Too many SSL alert thresholds.");
        _sslAlertThresholds = serializedThresholds;

        if (!monitoringEnabled)
        {
            NextScheduledAt = null;
        }
        else if (frequencyChanged || wasDisabled)
        {
            // Reschedule from now based on the new frequency
            NextScheduledAt = DateTime.UtcNow.Add(scanFrequency.ToTimeSpan());
        }
        Touch();
    }

    // Called by the worker after each scan run
    public void RecordMonitoringRun()
    {
        LastMonitoredAt = DateTime.UtcNow;
        NextScheduledAt = DateTime.UtcNow.Add(ScanFrequency.ToTimeSpan());
        Touch();
    }

    public void Disable()
    {
        MonitoringEnabled = false;
        NextScheduledAt = null;
        Touch();
    }

    public void Enable()
    {
        MonitoringEnabled = true;
        NextScheduledAt = DateTime.UtcNow.Add(ScanFrequency.ToTimeSpan());
        Touch();
    }

    // Convenience — parsed thresholds for the SSL expiry checker
    public IReadOnlyList<int> GetSslAlertThresholds() =>
        _sslAlertThresholds
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToList();
}