using Application.Interfaces;
using Application.Features.Domain;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class SubscriptionRepository(VulnWatchDbContext db)
    : BaseRepository<Subscription>(db), ISubscriptionRepository
{
    public Task<Subscription?> GetActiveByUser(Guid userId, CancellationToken ct = default) =>
        Db.Subscriptions
            .FirstOrDefaultAsync(d =>
                d.UserId == userId &&
                d.Status != SubscriptionStatus.Active, ct);
}