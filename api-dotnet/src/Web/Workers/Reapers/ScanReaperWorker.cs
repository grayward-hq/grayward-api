using Application.Interfaces;
using Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace Web.Workers.Reapers;

/// <summary>
/// Fails scans that have been Running or Queued for longer than their respective
/// timeout windows. Runs every 10 minutes.
///
/// Timeouts:
///   Running  — 1 hour  (worker picked it up but never completed)
///   Queued   — 5 minutes (worker never picked it up, e.g. Redis was down) 
/// </summary>
public class ScanReaperWorker(
    ILogger<ScanReaperWorker> logger,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    private static readonly TimeSpan CheckInterval   = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RunningTimeout  = TimeSpan.FromHours(1);
    private static readonly TimeSpan QueuedTimeout   = TimeSpan.FromMinutes(5);

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

            await Task.Delay(CheckInterval, ct);
        }
    }

    private async Task ReapAbandonedScans(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IVulnWatchDbContext>();

        

        // Running scans that started more than RunningTimeout ago
        var runningCutoff = DateTime.UtcNow - RunningTimeout;
        var stalledRunning = await context.Scans
        .Where(s => s.Status == ScanStatus.Running
         && (s.StartedAt == null || s.StartedAt < runningCutoff))
            .ToListAsync(ct);

        // Queued scans created more than QueuedTimeout ago (worker never picked them up)
        var queuedCutoff = DateTime.UtcNow - QueuedTimeout;
        var abandonedQueued = await context.Scans
            .Where(s => s.Status == ScanStatus.Queued
                     && s.CreatedAt < queuedCutoff)
            .ToListAsync(ct);

        var total = stalledRunning.Count + abandonedQueued.Count;

        if (total == 0)
        {
            logger.LogDebug("ScanReaperWorker tick — no abandoned scans found");
            return;
        }

        foreach (var scan in stalledRunning)
        {
            scan.Fail();
            // logger.LogWarning(
            //     "Reaped stalled Running scan {ScanId} — started at {StartedAt:u}, exceeded {Timeout}h timeout",
            //     scan.Id, scan.StartedAt, RunningTimeout.TotalHours);
        }

        foreach (var scan in abandonedQueued)
        {
            scan.Fail();
            // logger.LogWarning(
            //     "Reaped abandoned Queued scan {ScanId} — created at {CreatedAt:u}, exceeded {Timeout}h timeout",
            //     scan.Id, scan.CreatedAt, QueuedTimeout.TotalHours);
        }

        await context.SaveChangesAsync(ct);

        logger.LogInformation(
            "ScanReaperWorker reaped {Running} stalled running + {Queued} abandoned queued scan(s)",
            stalledRunning.Count, abandonedQueued.Count);
    }
}