using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;

namespace Tests.Infrastructure.Services;

public sealed class FakeQuotaService : IQuotaService
{
    public Task EnsureCanOnboard(Guid userId, ResourceKind kind, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
    public Task<Result<Scan>> ReserveScanSlot(
        Guid userId,
        ResourceKind resourceKind,
        Scan scan,
        CancellationToken ct = default)
    {
        return Task.FromResult(Result<Scan>.Success(scan));
    }
}