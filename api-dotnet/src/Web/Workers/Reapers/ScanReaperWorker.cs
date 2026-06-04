using Application.Interfaces;
using Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace Web.Workers.Reapers;

public class ScanReaperWorker(
    ILogger<ScanReaperWorker> logger,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ReapAbandonedScans(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "ScanReaperWorker tick failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(10), ct);
        }
    }

    private async Task ReapAbandonedScans(CancellationToken ct)
    {
        logger.LogInformation(
            "ScanReaperWorker tick — reaping abandoned scans");
            
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IVulnWatchDbContext>();

        var stalledRunning = await context.Scans
            .Where(s => s.Status == ScanStatus.Running
                     && s.UpdatedAt < DateTime.UtcNow.AddMinutes(-15))
            .ToListAsync(ct);

        var abandonedQueued = await context.Scans
            .Where(s => s.Status == ScanStatus.Queued
                     && s.CreatedAt < DateTime.UtcNow.AddMinutes(-5))
            .ToListAsync(ct);

        foreach (var scan in stalledRunning.Concat(abandonedQueued))
        {
            scan.Fail();
            logger.LogWarning("Reaped abandoned scan {ScanId} (was {Status})",
                scan.Id, scan.Status);
        }

        await context.SaveChangesAsync(ct);
    }
}