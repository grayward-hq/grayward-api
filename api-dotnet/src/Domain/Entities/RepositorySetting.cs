using Domain.Enums;
using Domain.Common;

namespace Domain.Entities;

public class RepositorySetting : EntityBase
{
    public Guid RepositoryId { get; private set; }
    public bool PeriodicScanEnabled { get; private set; }
    public ScanFrequency PeriodicScanFrequency { get; private set; }
    public DateTime? NextScanDueAt { get; private set; }
    public DateTime? LastScanAt { get; private set; }
    public bool EventScanEnabled { get; private set; }
    public RepositoryEventTrigger Triggers { get; private set; }
    public AlertChannel AlertChannels { get; private set; }
    public uint Version { get; private set; }

    private RepositorySetting() { }

    public static RepositorySetting CreateDefault(Guid repositoryId) => new()
    {
        RepositoryId          = repositoryId,
        PeriodicScanEnabled   = false,             
        PeriodicScanFrequency = ScanFrequency.Daily,
        NextScanDueAt         = null,              
        LastScanAt            = null,
        EventScanEnabled      = false,             
        Triggers              = RepositoryEventTrigger.None,
        AlertChannels         = AlertChannel.Email,
    };

    public void EnablePeriodicScan(ScanFrequency frequency, DateTime nowUtc)
    {
        PeriodicScanEnabled   = true;
        PeriodicScanFrequency = frequency;
        NextScanDueAt         = (LastScanAt ?? nowUtc) + frequency.ToTimeSpan();
    }

    public void DisablePeriodicScan()
    {
        PeriodicScanEnabled = false;
        NextScanDueAt       = null; 
    }

    public void ChangeFrequency(ScanFrequency frequency, DateTime nowUtc)
    {
        PeriodicScanFrequency = frequency;
        if (PeriodicScanEnabled)          
            NextScanDueAt = (LastScanAt ?? nowUtc) + frequency.ToTimeSpan();
    }

    public void EnableEventScan(RepositoryEventTrigger triggers)
    {
        if (triggers == RepositoryEventTrigger.None)
            throw new DomainException("Enable at least one event trigger (Push or PullRequest).");
        EventScanEnabled = true;
        Triggers         = triggers;
    }

    public void DisableEventScan()
    {
        EventScanEnabled = false;
        Triggers         = RepositoryEventTrigger.None;
    }

    public void SetAlertChannels(AlertChannel channels) => AlertChannels = channels;

    public void RecordScanCompleted(DateTime nowUtc)
    {
        LastScanAt = nowUtc;
        if (PeriodicScanEnabled)
            NextScanDueAt = nowUtc + PeriodicScanFrequency.ToTimeSpan();
    }

    public void Configure(
        bool periodicEnabled, ScanFrequency frequency,
        bool eventEnabled, RepositoryEventTrigger triggers,
        AlertChannel alertChannels, DateTime nowUtc)
    {
        if (periodicEnabled) EnablePeriodicScan(frequency, nowUtc);
        else                 DisablePeriodicScan();
        
        PeriodicScanFrequency = frequency;

        if (eventEnabled) EnableEventScan(triggers);
        else              DisableEventScan();

        SetAlertChannels(alertChannels);
    }
}