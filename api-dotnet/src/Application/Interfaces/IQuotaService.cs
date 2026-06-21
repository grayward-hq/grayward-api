
using Application.Features.Integrations.Slack.DTOs;
using Domain.Common;
using Domain.Enums;

namespace Application.Interfaces;
public interface IQuotaService
{
    // Task EnsureCanOnboardRepo(Guid userId, CancellationToken ct);
    // Task EnsureCanOnboardDomain(Guid userId, CancellationToken ct);
    // // Task EnsureScheduleAllowedAsync(Guid userId, ScanSchedule schedule, CancellationToken ct);
    // // Task EnsureChannelsAllowedAsync(Guid userId, IEnumerable<AlertChannel> channels, CancellationToken ct);
    // Task EnsureCanCreateDomainScan(Guid userId, CancellationToken ct);   // also reserves a slot
    // Task EnsureCanCreateRepositoryScan(Guid userId, CancellationToken ct);   // also reserves a slot
    Task EnsureCanOnboard(Guid userId, ResourceKind kind, CancellationToken ct);
    Task EnsureCanStartScan(Guid userId, ResourceKind kind, CancellationToken ct);
}