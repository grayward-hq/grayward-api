using Domain.Enums;

namespace Domain.Entities;

public class Integration : EntityBase
{
    public Guid UserId { get; private set; }
    public IntegrationProvider Provider { get; private set; } = default!;
    public string InstallationId { get; private set; } = default!;
    public IntegrationStatus Status { get; private set; }

    public Dictionary<string, string> Metadata { get; private set; } = new();

    public User User { get; private set; } = default!;

    private Integration() { }

    public static Integration Create(Guid userId, IntegrationProvider provider, string installationId, Dictionary<string, string>? metadata = null)
        => new()
        {
            UserId = userId,
            Provider = provider,
            InstallationId = installationId,
            Metadata = metadata ?? new(),
            Status = IntegrationStatus.INACTIVE,
        };

    public void Activate()
    {
        Status = IntegrationStatus.ACTIVE;
        Touch();
    }

    public void Deactivate()
    {
        Status = IntegrationStatus.INACTIVE;
        Touch();
    }

    public void UpdateInstallation(string installationId)
    {
        InstallationId = installationId;
        Touch();
    }

    public void UpsertMetadata(Dictionary<string, string> updates)
    {
        foreach (var (key, value) in updates)
            Metadata[key] = value;
        Touch();
    }

    public string? GetMetadata(string key)
        => Metadata.TryGetValue(key, out var value) ? value : null;
}
