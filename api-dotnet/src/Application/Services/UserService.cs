using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

public class UserService(
    ISubscriptionRepository subs,
    INotificationPreferencesRepository prefs) : IUserService
{
    public async Task<Result<bool>> ProvisionNewUser(User user, CancellationToken ct)
    {
        var createdSomething = false;

        var existingPrefs = await prefs.ExistsForUser(user.Id, ct);
        if (!existingPrefs)
        {
            var defaultPrefs = NotificationPreferences.Create(user.Id, emailAlerts: true);
            await prefs.AddAsync(defaultPrefs, ct);
            createdSomething = true;
        }

        var existingSub = await subs.GetActiveByUser(user.Id, ct);
        if (existingSub is null)
        {
            var subscription = Subscription.Create(
                user.Id,
                PlanCode.Free,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddYears(100));

            await subs.AddAsync(subscription, ct);
            createdSomething = true;
        }

        await prefs.SaveChangesAsync(ct);

        return Result<bool>.Success(createdSomething);
    }
}