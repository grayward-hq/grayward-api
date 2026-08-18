
using Application.Features.Integrations.Slack.DTOs;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;

namespace Application.Interfaces;
public interface IQuotaService
{
    Task EnsureCanOnboard(Guid userId, ResourceKind kind, CancellationToken ct);
    // Task EnsureCanStartScan(Guid userId, ResourceKind kind, CancellationToken ct);
    Task<Result<Scan>> ReserveScanSlot(Guid userId, ResourceKind kind, Scan scan, CancellationToken ct);
}