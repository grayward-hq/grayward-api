using System.Text.Json;
using Application.Features.Alerts;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Events;
using Domain.Meta;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using Web.Hubs;

namespace Web.Consumers;

public record DomainIntel(
    Guid ScanId,
    Guid DomainId,
    string DomainName,
    Guid RequestedBy,
    int SecurityScore,
    string Status,
    DateTimeOffset CompletedAt,
    string? Error);

public class DomainIntelConsumer : BackgroundService
{
    private readonly string _queue;
    private readonly IConnectionMultiplexer _redis;
    private readonly IHubContext<ScanHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DomainIntelConsumer> _logger;

    public DomainIntelConsumer(
        IConnectionMultiplexer redis,
        IHubContext<ScanHub> hub,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<DomainIntelConsumer> logger)
    {
        _redis = redis;
        _hub = hub;
        _scopeFactory = scopeFactory;
        _queue = config["Worker:DomainIntelQueue"] ?? "domain-intel";
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("DomainIntelConsumer listening on {Queue}", _queue);
        var db = _redis.GetDatabase();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // BLPOP equivalent — poll with timeout
                var result = await db.ListRightPopAsync(_queue);

                if (result.IsNullOrEmpty)
                {
                    await Task.Delay(500, ct);
                    continue;
                }

                // var raw = result.ToString();
                // _logger.LogInformation("Domain Raw scan result payload: {Payload}", raw);

                // Console.WriteLine(raw);
                // Console.WriteLine(raw[0]);

                var json = JsonSerializer.Deserialize<string>(result.ToString());

                var message = JsonSerializer.Deserialize<DomainIntel>(
                    json!,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (message is null) continue;

                _logger.LogInformation("Scan result received for {ScanId}", message.ScanId);

                //1. emit to the user's webhook
                await _hub.Clients
                    .Group($"user:{message.RequestedBy}")
                    .SendAsync("ScanCompleted", message, ct);

                //2. dispatch alerts
                await DispatchScanAlerts(message, ct);


            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error processing scan result");
                await Task.Delay(1000, ct);
            }
        }
    }

    private async Task DispatchScanAlerts(DomainIntel message, CancellationToken ct)
    {
        if (!Guid.TryParse(message.ScanId.ToString(), out var scanId) ||
            !Guid.TryParse(message.DomainId.ToString(), out var domainId) ||
            !Guid.TryParse(message.RequestedBy.ToString(), out var userId))
        {
            _logger.LogWarning(
                "DomainIntelConsumer — invalid IDs in message, cannot dispatch alerts. " +
                "ScanId: {ScanId}, DomainId: {DomainId}, RequestedBy: {RequestedBy}",
                message.ScanId, message.DomainId, message.RequestedBy);
            return;
        }

        // _logger.LogInformation(
        //     "Dispatching alerts for Scan: {ScanMessage}",
        //     message);

        using var scope = _scopeFactory.CreateScope();
        var scanRepo = scope.ServiceProvider.GetRequiredService<IScanRepository>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AlertDispatcher>();
        var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var integrationsRepo = scope.ServiceProvider.GetRequiredService<IIntegrationRepository>();
        var slackService = scope.ServiceProvider.GetRequiredService<ISlackService>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var alertService = scope.ServiceProvider.GetRequiredService<IAlertService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var scan = await scanRepo.FindByIdWithFindings(scanId, ct);

        if (scan is null || scan.Status != ScanStatus.Completed)
        {
            return;
        }

        var findingSeverities = scan.Findings
            .Where(f => f.Status == FindingStatus.Open)
            .Select(f => f.Severity)
            .ToList();

        await dispatcher.DispatchAsync(new ScanCompletedEvent(
            ScanId: scanId,
            DomainId: domainId,
            UserId: userId,
            DomainName: message.DomainName,
            SecurityScore: scan.SecurityScore ?? 0,
            FindingSeverities: findingSeverities), ct);

        await FlushPendingAlertsAsync(
            scope, alertRepo, alertService,userId, ct);
    }

    // Flush only this user's alerts immediately after a scan completes —
    // the AlertOutboxProcessor will handle everything else on its normal cadence
    private async Task FlushPendingAlertsAsync(
        IServiceScope scope,
        IAlertRepository alertRepo,
        IAlertService alertService,
        Guid userId,
        CancellationToken ct)
    {
        var pending = await alertRepo.GetPendingByUser(userId, batchSize: 20, ct);

        if (pending.Count == 0)
        {
            return;
        }

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
                        alert.MarkFailed($"Channel {alert.Channel} not implemented.");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to flush alert {AlertId} via {Channel}",
                    alert.Id, alert.Channel);
                alert.MarkFailed(ex.Message);
            }
        }

        await alertRepo.SaveChangesAsync(ct);
    }
}