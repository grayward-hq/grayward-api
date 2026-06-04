

using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class NotificationPreferencesRepository(VulnWatchDbContext db)
    : BaseRepository<NotificationPreferences>(db), INotificationPreferencesRepository
{
    public Task<bool> ExistsForUser(Guid userId, CancellationToken ct) =>
        Db.NotificationPreferences
            .AnyAsync(n => n.UserId == userId, ct);
    public Task<NotificationPreferences?> GetByUserId(Guid userId, CancellationToken ct) => 
        Db.NotificationPreferences
            .FirstOrDefaultAsync(d => d.UserId == userId, ct);
    
}