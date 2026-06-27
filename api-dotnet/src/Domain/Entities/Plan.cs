

using Domain.Enums;

namespace Domain.Entities;

public sealed record ResourceLimits(
    int MaxOnboarded,
    int MaxScansPerMonth,
    int MaxConcurrentScans,
    TimeSpan MinScanInterval);

public sealed record Plan(
    PlanCode Code,
    ResourceLimits Domains,
    ResourceLimits Repositories,
    IReadOnlySet<AlertChannel> AllowedChannels)
{
    public ResourceLimits For(ResourceKind kind) => kind switch
    {
        ResourceKind.Domain     => Domains,
        ResourceKind.Repository => Repositories,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}