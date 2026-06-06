using Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Interfaces;

public interface IAlertService
{
    Task DeliverEmailAsync(
        IServiceScope scope,
        Alert alert,
        CancellationToken ct);

    Task DeliverSlackAsync(
        Alert alert,
        CancellationToken ct);

    Task<string> ResolveEmailAsync(
        IServiceScope scope,
        Guid userId,
        CancellationToken ct);

    object BuildSlackBlocks(Alert alert);
}