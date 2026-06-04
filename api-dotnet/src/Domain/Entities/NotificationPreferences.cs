namespace Domain.Entities;

public class NotificationPreferences : EntityBase
{
    public Guid UserId { get; private set; }
    public bool EmailAlerts { get; private set; }
    public bool SlackAlerts { get; private set; }
    public bool PushNotifications { get; private set; }

    public User User { get; private set; } = default!;

    private NotificationPreferences() { }

    public static NotificationPreferences Create(Guid userId, bool emailAlerts = true, bool slackAlerts = false, bool pushNotifications = false)
        => new()
        {
            UserId = userId,
            EmailAlerts = emailAlerts,
            SlackAlerts = slackAlerts,
            PushNotifications = pushNotifications,
        };

    public void Update(bool emailAlerts, bool slackAlerts, bool pushNotifications)
    {
        EmailAlerts = emailAlerts;
        SlackAlerts = slackAlerts;
        PushNotifications = pushNotifications;
        Touch();
    }
}
