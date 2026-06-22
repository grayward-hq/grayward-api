using Application.Interfaces;
using Application.Features.Domain;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class SubscriptionRepository(VulnWatchDbContext db)
    : BaseRepository<Subscription>(db), ISubscriptionRepository
{
    public Task<Subscription?> GetActiveByUserForUpdate(Guid userId, CancellationToken ct)
        => Db.Subscriptions
            .FromSql($@"SELECT * FROM subscriptions
                        WHERE user_id = {userId} AND status = 'Active'
                        LIMIT 1 FOR UPDATE")
            .ToListAsync(ct)
            .ContinueWith(t => t.Result.FirstOrDefault(), ct);

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