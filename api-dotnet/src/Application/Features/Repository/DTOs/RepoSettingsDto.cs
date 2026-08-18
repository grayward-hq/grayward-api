using Domain.Enums;

namespace Application.Features.Repository.DTOs;

public record RepoSettingsDto(
    bool PeriodicScanEnabled,
    ScanFrequency PeriodicScanFrequency,
    bool EventScanEnabled,
    RepositoryEventTrigger Triggers,
    AlertChannel AlertChannels,
    DateTime? NextScanDueAt,
    DateTime? LastScanAt,
    string Version);  