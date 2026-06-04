using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Web.Workers.Monitoring;

public sealed class MonitoringWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<MonitoringWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BusyInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MinInterval  = TimeSpan.FromSeconds(15);
    private const int BatchSize = 20; // tune to your expected concurrent domain volume

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("MonitoringWorker started");

        await Task.Delay(TimeSpan.FromSeconds(30), ct);

        while (!ct.IsCancellationRequested)
        {
            int processed = 0;
            try
            {
                processed = await RunTickAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "MonitoringWorker tick failed");
            }

            var delay = processed == 0        ? IdleInterval
                      : processed >= BatchSize ? MinInterval   // batch was full — likely more queued
                      :                          BusyInterval;

            logger.LogDebug(
                "MonitoringWorker processed {Count} domain(s) — next tick in {Delay}",
                processed, delay);

            await Task.Delay(delay, ct);
        }

        logger.LogInformation("MonitoringWorker stopped");
    }

    private async Task<int> RunTickAsync(CancellationToken ct)
    {
        List<Domain.Entities.DomainSettings> due;
        using (var fetchScope = scopeFactory.CreateScope())
        {
            var settingsRepo = fetchScope.ServiceProvider
                .GetRequiredService<IDomainSettingsRepository>();
            due = await settingsRepo.GetDueForScan(DateTime.UtcNow, BatchSize, ct);
        }

        if (due.Count == 0)
        {
            logger.LogDebug("MonitoringWorker tick — no domains due");
            return 0;
        }

        logger.LogInformation(
            "MonitoringWorker tick — {Count} domain(s) due for monitoring",
            due.Count);

        var semaphore = new SemaphoreSlim(5);

        var tasks = due.Select(async settings =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                using var scope = scopeFactory.CreateScope();

                var settingsRepo  = scope.ServiceProvider.GetRequiredService<IDomainSettingsRepository>();
                var scanDispatch  = scope.ServiceProvider.GetRequiredService<ScanDispatchService>();
                var sslCheck      = scope.ServiceProvider.GetRequiredService<SslExpiryCheckService>();
                var ownershipCheck = scope.ServiceProvider.GetRequiredService<OwnershipCheckService>();

                await ProcessDomainAsync(
                    settings, scanDispatch, sslCheck, ownershipCheck,
                    settingsRepo, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error processing domain {DomainId} in monitoring worker",
                    settings.DomainId);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return due.Count;
    }

    private async Task ProcessDomainAsync(
        Domain.Entities.DomainSettings settings,
        ScanDispatchService scanDispatch,
        SslExpiryCheckService sslCheck,
        OwnershipCheckService ownershipCheck,
        IDomainSettingsRepository settingsRepo,
        CancellationToken ct)
    {
        var domainName = settings.Domain.DomainName;

        logger.LogDebug("Processing monitoring for {Domain}", domainName);

        // Run the three checks — each is independent, failures are isolated
        await RunGuarded(() => scanDispatch.DispatchAsync(settings, ct),
            "scan dispatch", domainName);

        await RunGuarded(() => sslCheck.CheckAsync(settings, ct),
            "SSL expiry check", domainName);

        await RunGuarded(() => ownershipCheck.CheckAsync(settings, ct),
            "ownership check", domainName);

        // Always advance NextScheduledAt even if some checks failed —
        // we don't want a broken domain to block the queue
        settings.RecordMonitoringRun();
        await settingsRepo.SaveChangesAsync(ct);

        logger.LogInformation(
            "Monitoring scheduled for {Domain} — next run at {Next:u}",
            domainName, settings.NextScheduledAt);
    }

    private async Task RunGuarded(
        Func<Task> action,
        string stepName,
        string domainName)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Monitoring step '{Step}' failed for {Domain}",
                stepName, domainName);
        }
    }
}