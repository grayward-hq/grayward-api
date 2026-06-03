using Application.Features.Profile.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using MediatR;

namespace Application.Features.Profile;

public record UpdateNotificationPreferencesCommand(
    bool EmailAlerts,
    bool SlackAlerts,
    bool PushNotifications) : IRequest<Result<NotificationPreferencesDto>>;

public class UpdateNotificationPreferencesHandler(
    INotificationPreferencesRepository notifPrefs,
    ICurrentUser currentUser)
    : IRequestHandler<UpdateNotificationPreferencesCommand, Result<NotificationPreferencesDto>>
{
    public async Task<Result<NotificationPreferencesDto>> Handle(
    UpdateNotificationPreferencesCommand cmd, CancellationToken ct)
    {
        var prefs = await notifPrefs.GetByUserId(currentUser.UserId, ct);

        if (prefs is null)
        {
            prefs = NotificationPreferences.Create(currentUser.UserId, cmd.EmailAlerts, cmd.SlackAlerts, cmd.PushNotifications);
            await notifPrefs.AddAsync(prefs, ct);
        }

        prefs.Update(cmd.EmailAlerts, cmd.SlackAlerts, cmd.PushNotifications);
        await notifPrefs.SaveChangesAsync(ct);

        return Result<NotificationPreferencesDto>.Success(new NotificationPreferencesDto(
            prefs.EmailAlerts,
            prefs.SlackAlerts,
            prefs.PushNotifications));
    }
}