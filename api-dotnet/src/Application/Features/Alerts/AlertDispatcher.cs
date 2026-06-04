using Application.Features.Alerts.ScanCompleted;
using Application.Features.Alerts.SslExpiry;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Features.Alerts;

public class AlertDispatcher
{
    private readonly IAlertRepository _alerts;
    private readonly INotificationPreferencesRepository _prefs;
    private readonly IDomainSettingsRepository _domainSettings;
    private readonly ILogger<AlertDispatcher> _logger;

    public AlertDispatcher(
        IAlertRepository alerts,
        INotificationPreferencesRepository prefs,
        IDomainSettingsRepository domainSettings,
        ILogger<AlertDispatcher> logger)
    {
        _alerts = alerts;
        _prefs = prefs;
        _domainSettings = domainSettings;
        _logger = logger;
    }

    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct)
    {
        switch (domainEvent)
        {
            case SslExpiryEvent e:
                await HandleSslExpiry(e, ct);
                break;
            case ScanCompletedEvent e:
                await HandleScanCompleted(e, ct);
                break;
            default:
                _logger.LogWarning("No handler registered for event type {EventType}",
                    domainEvent.GetType().Name);
                break;
        }
    }

    private async Task HandleSslExpiry(SslExpiryEvent e, CancellationToken ct)
    {
        var domainSettings = await _domainSettings.GetByDomainId(e.DomainId, ct);
        var channels = domainSettings is not null
            ? ResolveDomainChannels(domainSettings.NotificationChannel)
            : [AlertChannel.Email];

        var deduplicationKey = DateTime.UtcNow.ToString("yyyy-MM-dd");

        foreach (var channel in channels)
        {
            var alreadyExists = await _alerts.ExistsForToday(
                e.UserId, AlertType.SslExpiry, e.DomainId, channel, deduplicationKey, ct);

            if (alreadyExists)
            {
                continue;
            }

            var alert = SslExpiryAlertFactory.Create(e, channel);
            await _alerts.AddAsync(alert, ct);

            try
            {
                await _alerts.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                _alerts.DetachUnsavedAlerts();
            }
        }
    }

    private async Task HandleScanCompleted(ScanCompletedEvent e, CancellationToken ct)
    {
        var domainSettings = await _domainSettings.GetByDomainId(e.DomainId, ct);
        var channels = domainSettings is not null
            ? ResolveDomainChannels(domainSettings.NotificationChannel)
            : [AlertChannel.Email];

        var deduplicationKey = e.ScanId.ToString();

        foreach (var channel in channels)
        {
            var alreadyExists = await _alerts.ExistsForToday(
                e.UserId, AlertType.ScanCompleted, e.DomainId, channel, deduplicationKey, ct);

            if (alreadyExists)
            {
                _logger.LogDebug(
                    "Scan completed alert for {DomainName} via {Channel} already exists — skipping",
                    e.DomainName, channel);
                continue;
            }

            var alert = ScanCompletedAlertFactory.Create(e, channel);

            await _alerts.AddAsync(alert, ct);
            try
            {
                await _alerts.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                _alerts.DetachUnsavedAlerts();
            }
        }
    }

    private static List<AlertChannel> ResolveDomainChannels(AlertChannel channel)
    {
        var channels = new List<AlertChannel>();
        if (channel.HasFlag(AlertChannel.Email)) channels.Add(AlertChannel.Email);
        if (channel.HasFlag(AlertChannel.Slack)) channels.Add(AlertChannel.Slack);
        if (channel.HasFlag(AlertChannel.Push)) channels.Add(AlertChannel.Push);
        return channels.Count > 0 ? channels : [AlertChannel.Email];
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;

        if (inner?.GetType().FullName == "Npgsql.PostgresException")
        {
            const string uniqueViolationSqlState = "23505";
            var sqlState = inner.GetType()
                .GetProperty("SqlState")?
                .GetValue(inner) as string;
            return sqlState == uniqueViolationSqlState;
        }

        return false;
    }
}