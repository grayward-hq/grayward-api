using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Web.Workers.Monitoring.Jobs;

namespace Web.Workers.Monitoring;

public sealed class MonitoringWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<MonitoringWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BusyInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(15);
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

            var delay = processed == 0 ? IdleInterval
                      : processed >= BatchSize ? MinInterval   // batch was full — likely more queued
                      : BusyInterval;

            logger.LogDebug(
                "MonitoringWorker processed {Count} domain(s) — next tick in {Delay}",
                processed, delay);

            await Task.Delay(delay, ct);
        }

        logger.LogInformation("MonitoringWorker stopped");
    }

    private async Task<int> RunTickAsync(CancellationToken ct)
    {
        List<Guid> dueIds;

        // Fetch only the IDs in a short-lived scope
        using (var fetchScope = scopeFactory.CreateScope())
        {
            var settingsRepo = fetchScope.ServiceProvider
                .GetRequiredService<IDomainSettingsRepository>();
            var due = await settingsRepo.GetDueForScan(DateTime.UtcNow, BatchSize, ct);
            dueIds = due.Select(s => s.DomainId).ToList();
        }

        if (dueIds.Count == 0)
        {
            logger.LogDebug("MonitoringWorker tick — no domains due");
            return 0;
        }

        logger.LogInformation(
            "MonitoringWorker tick — {Count} domain(s) due for monitoring",
            dueIds.Count);

        var semaphore = new SemaphoreSlim(5);

        var tasks = dueIds.Select(async domainId =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                // Each domain gets its own scope — fetch, process, and save all within it
                using var scope = scopeFactory.CreateScope();

                var settingsRepo = scope.ServiceProvider.GetRequiredService<IDomainSettingsRepository>();
                var scanDispatch = scope.ServiceProvider.GetRequiredService<ScanDispatchService>();
                var sslCheck = scope.ServiceProvider.GetRequiredService<SslExpiryCheckService>();
                var ownershipCheck = scope.ServiceProvider.GetRequiredService<OwnershipCheckService>();
                var brandProtection = scope.ServiceProvider.GetRequiredService<BrandProtectionCheckService>();
                var breachMonitoring = scope.ServiceProvider.GetRequiredService<BreachMonitoringService>();
    
                // Re-fetch within this scope so SaveChangesAsync tracks the right object
                var settings = await settingsRepo.GetByDomainId(domainId, ct);
                if (settings is null) return;

                

                await ProcessDomainAsync(
                    settings, scanDispatch, sslCheck, ownershipCheck,
                    brandProtection, breachMonitoring, settingsRepo, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error processing domain {DomainId} in monitoring worker",
                    domainId);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return dueIds.Count;
    }

    private async Task ProcessDomainAsync(
        Domain.Entities.DomainSettings settings,
        ScanDispatchService scanDispatch,
        SslExpiryCheckService sslCheck,
        OwnershipCheckService ownershipCheck,
        BrandProtectionCheckService brandProtectionService,
        BreachMonitoringService breachMonitoringService,
        IDomainSettingsRepository settingsRepo,
        CancellationToken ct)
    {
        var domain = settings.Domain;
        var domainName = settings.Domain.DomainName;
        var domainId = settings.Domain.Id;

        logger.LogDebug("Processing monitoring for {Domain}", domainName);

        // Run the three checks — each is independent, failures are isolated
        await RunGuarded(() => scanDispatch.DispatchAsync(settings, ct),
            "scan dispatch", domainName);

        await RunGuarded(() => sslCheck.CheckAsync(settings, ct),
            "SSL expiry check", domainName);

        await RunGuarded(() => ownershipCheck.CheckAsync(settings, ct),
            "ownership check", domainName);

        await RunGuarded(() => brandProtectionService.CheckAsync(domain, ct),
            "brand protection check", domainName);

        // await RunGuarded(() => breachMonitoringService.CheckAsync(domainId, ct),
        //     "breach monitoring check", domainName);

        if (settings.NextBreachCheckAt is null || settings.NextBreachCheckAt <= DateTime.UtcNow)
        {
            await RunGuarded(() => breachMonitoringService.CheckAsync(domain, ct), "breach monitoring", domainName);
            settings.NextBreachCheckAt = DateTime.UtcNow.AddHours(24);
        }

        // Always advance NextScheduledAt even if some checks failed —
        // we don't want a broken domain to block the queue
        settings.RecordMonitoringRun();
        await settingsRepo.SaveChangesAsync(ct);

        // logger.LogInformation(
        //     "Monitoring scheduled for {Domain} — next run at {Next:u}",
        //     domainName, settings.NextScheduledAt);
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