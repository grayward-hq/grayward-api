
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Meta;
using Microsoft.AspNetCore.Identity;

namespace Web.Workers.Alerts;

public class AlertOutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlertOutboxProcessor> _logger;
    private static readonly TimeSpan IdleInterval  = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan BusyInterval  = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinInterval   = TimeSpan.FromSeconds(10);
    private const int BatchSize = 50;

    public AlertOutboxProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<AlertOutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), ct);

        while (!ct.IsCancellationRequested)
        {
            int processed = 0;
            try
            {
                processed = await ProcessBatch(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Alert outbox processor error");
            }

             var delay = processed == 0         ? IdleInterval
                      : processed >= BatchSize ? MinInterval   // batch was full — likely more queued
                      :                          BusyInterval;

            _logger.LogDebug(
                "Alert outbox processed {Count} alert(s) — next tick in {Delay}",
                processed, delay);

            await Task.Delay(delay, ct);
        }
    }

    private async Task<int> ProcessBatch(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var alerts       = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var email        = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var integrations    = scope.ServiceProvider.GetRequiredService<IIntegrationRepository>();
        var slackService = scope.ServiceProvider.GetRequiredService<ISlackService>();
        var alertService = scope.ServiceProvider.GetRequiredService<IAlertService>();
        var pending = await alerts.GetPendingAsync(batchSize: BatchSize, ct);

        if (pending.Count == 0)
            return 0;

        foreach (var alert in pending)
        {
            try
            {
                switch (alert.Channel)
                {
                    case AlertChannel.Email:
                        await alertService.DeliverEmailAsync(scope, alert, ct);
                        break;
                    case AlertChannel.Slack:
                        await alertService.DeliverSlackAsync(alert, ct);
                        break;
                    default:
                        _logger.LogWarning(
                            "Unhandled alert channel {Channel} for alert {AlertId}",
                            alert.Channel, alert.Id);
                        alert.MarkFailed($"Channel {alert.Channel} not implemented.");
                        break;
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send alert {AlertId}", alert.Id);
                alert.MarkFailed("Delivery failed. See logs for details.");
            }
        }

        await alerts.SaveChangesAsync(ct);
        return pending.Count;
    }
}