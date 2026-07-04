using Application.Interfaces;
using Application.Features.Domain;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class SubscriptionRepository(VulnWatchDbContext db)
    : BaseRepository<Subscription>(db), ISubscriptionRepository
{
    public async Task<Subscription?> GetActiveByUserForUpdate(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await Db.Subscriptions
            .FromSqlInterpolated($@"
                SELECT *
                FROM ""Subscriptions""
                WHERE ""UserId"" = {userId}
                AND ""Status"" = {"Active"}
                AND ""CurrentPeriodStart"" <= {now}
                AND ""CurrentPeriodEnd"" >= {now}
                FOR UPDATE")
            .FirstOrDefaultAsync(ct);
    }

    public Task<Subscription?> GetActiveByUser(Guid userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return Db.Subscriptions.FirstOrDefaultAsync(d =>
            d.UserId == userId &&
            d.Status == SubscriptionStatus.Active &&
            d.CurrentPeriodStart <= now &&
            d.CurrentPeriodEnd >= now, ct);
    }
}