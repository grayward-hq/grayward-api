using Application.Features.Alerts;
using Application.Features.Alerts.SslExpiry;

namespace Web.Workers.Alerts;

public class SslExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SslExpiryWorker> _logger;

    public SslExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<SslExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "SSL expiry worker cycle failed");
            }

            await Task.Delay(TimeSpan.FromHours(24), ct);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var checker = scope.ServiceProvider.GetRequiredService<SslExpiryChecker>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<AlertDispatcher>();

        _logger.LogInformation("SSL expiry worker running at {Now}", DateTimeOffset.UtcNow);
        var events = await checker.GetPendingAlerts(ct);

        // _logger.LogInformation("Found {Count} domains with expiring SSL certs", events.Count);

        foreach (var e in events)
        {
            try
            {
                await dispatcher.DispatchAsync(e, ct);
                // _logger.LogInformation(
                //     "SSL expiry alert dispatched for {DomainName}, {Days} days remaining",
                //     e.DomainName, e.DaysRemaining);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SSL expiry alert failed for domain {DomainId}", e.DomainId);
            }
        }
    }
}