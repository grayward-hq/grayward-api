using Application.Features.Alerts.DomainOwnershipWarning;
using Application.Features.Alerts.ScanCompleted;
using Application.Features.Alerts.SslExpiry;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Application.Features.Alerts;

public class AlertDispatcher(
    IAlertRepository alerts,
    INotificationPreferencesRepository prefs,
    IDomainSettingsRepository domainSettings,
    IConfiguration config,
    ILogger<AlertDispatcher> logger) : AlertHandlerBase(alerts, domainSettings, logger)
{
    private readonly INotificationPreferencesRepository _prefs = prefs;
    private readonly IConfiguration _config = config;
    private readonly ILogger<AlertDispatcher> _logger = logger;

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
            case DomainOwnershipWarningEvent e:
                await HandleOwnershipWarning(e, ct);
                break;
            default:
                _logger.LogWarning("No handler registered for event type {EventType}",
                    domainEvent.GetType().Name);
                break;
        }
    }

    private async Task HandleSslExpiry(SslExpiryEvent e, CancellationToken ct)
    {
        var channels = await ResolveChannelsAsync(e.DomainId, ct);
        var deduplicationKey = DateTime.UtcNow.ToString("yyyy-MM-dd");

        foreach (var channel in channels)
        {
            var alreadyExists = await Alerts.ExistsForToday(
                e.UserId, AlertType.SslExpiry, e.DomainId, channel, deduplicationKey, ct);

            if (alreadyExists)
            {
                continue;
            }

            var alert = SslExpiryAlertFactory.Create(e, channel);
            await SaveAlertGuarded(alert, ct);
        }
    }

    private async Task HandleScanCompleted(ScanCompletedEvent e, CancellationToken ct)
    {
        _logger.LogInformation(
            "Handling ScanCompleted event — Domain: {DomainName}, ScanId: {ScanId}, UserId: {UserId}",
            e.DomainName, e.ScanId, e.UserId);

        var channels = await ResolveChannelsAsync(e.DomainId, ct);
        var deduplicationKey = e.ScanId.ToString();

        if (channels.Count == 0)
        {
            _logger.LogWarning(
                "No notification channels configured for domain {DomainName} (ID: {DomainId}) — scan alerts will not be sent",
                e.DomainName, e.DomainId);
            return;
        }

        foreach (var channel in channels)
        {
            var alreadyExists = await Alerts.ExistsForToday(
                e.UserId, AlertType.ScanCompleted, e.DomainId, channel, deduplicationKey, ct);

            if (alreadyExists)
            {
                _logger.LogDebug(
                    "Scan completed alert for {DomainName} via {Channel} already exists — skipping",
                    e.DomainName, channel);
                continue;
            }

            _logger.LogInformation(
                "Creating ScanCompleted alert — Domain: {DomainName}, Channel: {Channel}, Severity: {FindingSeverities}",
                e.DomainName, channel, string.Join(",", e.FindingSeverities));

            var alert = ScanCompletedAlertFactory.Create(e, channel);
            await SaveAlertGuarded(alert, ct);
        }
    }

    private async Task HandleOwnershipWarning(
        DomainOwnershipWarningEvent e, CancellationToken ct)
    {
        var channels = await ResolveChannelsAsync(e.DomainId, ct);

        // Use stage as deduplication key so each stage only alerts once
        var deduplicationKey = $"ownership-{e.Stage}";

        foreach (var channel in channels)
        {
            var alreadyExists = await Alerts.ExistsForToday(
                e.UserId, AlertType.OwnershipWarning,
                e.DomainId, channel, deduplicationKey, ct);

            if (alreadyExists) continue;

             var alert = DomainOwnershipWarningAlertFactory.Create(e, channel, _config);
           

            await SaveAlertGuarded(alert, ct);
        }
    }

}