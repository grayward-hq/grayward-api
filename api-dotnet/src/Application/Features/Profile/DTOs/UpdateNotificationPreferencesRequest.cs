namespace Application.Features.Profile.DTOs;


public record UpdateNotificationPreferencesRequest(
    bool EmailAlerts,
    bool SlackAlerts,
    bool PushNotifications);