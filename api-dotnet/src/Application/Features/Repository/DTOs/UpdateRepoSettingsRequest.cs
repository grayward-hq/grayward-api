using Domain.Enums;

namespace Application.Features.Repository.DTOs;

public record UpdateRepoSettingsRequest(
    bool PeriodicScanEnabled,
    ScanFrequency PeriodicScanFrequency,
    bool EventScanEnabled,
    RepositoryEventTrigger Triggers,
    AlertChannel AlertChannels,
    string Version);