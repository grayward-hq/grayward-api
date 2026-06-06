using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Features.Alerts;

public abstract class AlertHandlerBase(
    IAlertRepository alerts,
    IDomainSettingsRepository domainSettings,
    ILogger? logger = null)
{
    protected readonly IAlertRepository Alerts = alerts;
    protected readonly IDomainSettingsRepository DomainSettings = domainSettings;
    protected readonly ILogger? Logger = logger;

    protected async Task<List<AlertChannel>> ResolveChannelsAsync(
        Guid domainId, CancellationToken ct)
    {
        var settings = await DomainSettings.GetByDomainId(domainId, ct);
        if (settings is null)
        {
            Logger?.LogWarning(
                "No DomainSettings found for domain {DomainId} — defaulting to Email channel",
                domainId);
            return [AlertChannel.Email];
        }

        var channels = ResolveDomainChannels(settings.NotificationChannel);
        Logger?.LogDebug(
            "Resolved {ChannelCount} notification channels for domain {DomainId}: {Channels}",
            channels.Count, domainId, string.Join(", ", channels));
        return channels;
    }

    protected async Task SaveAlertGuarded(Alert alert, CancellationToken ct)
    {
        await Alerts.AddAsync(alert, ct);
        try
        {
            await Alerts.SaveChangesAsync(ct);
            Logger?.LogDebug(
                "Alert saved successfully — Type: {AlertType}, Channel: {Channel}, User: {UserId}, Domain: {DomainId}",
                alert.Type, alert.Channel, alert.UserId, alert.DomainId);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            Logger?.LogInformation(
                "Alert already exists (duplicate key) — Type: {AlertType}, Channel: {Channel}, User: {UserId}, DeduplicationKey: {Key}",
                alert.Type, alert.Channel, alert.UserId, alert.DeduplicationKey);
            Alerts.DetachUnsavedAlerts();
        }
        catch (DbUpdateException ex)
        {
            Logger?.LogError(ex,
                "Failed to save alert — Type: {AlertType}, Channel: {Channel}, User: {UserId}, Domain: {DomainId}",
                alert.Type, alert.Channel, alert.UserId, alert.DomainId);
            throw;
        }
    }

    private static List<AlertChannel> ResolveDomainChannels(AlertChannel channel)
    {
        var channels = new List<AlertChannel>();
        if (channel.HasFlag(AlertChannel.Email)) channels.Add(AlertChannel.Email);
        if (channel.HasFlag(AlertChannel.Slack)) channels.Add(AlertChannel.Slack);
        if (channel.HasFlag(AlertChannel.Push))  channels.Add(AlertChannel.Push);
        return channels.Count > 0 ? channels : [AlertChannel.Email];
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner?.GetType().FullName != "Npgsql.PostgresException") return false;

        var sqlState = inner.GetType()
            .GetProperty("SqlState")?
            .GetValue(inner) as string;
        return sqlState == "23505";
    }
}