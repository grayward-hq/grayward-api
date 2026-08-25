using Application.Interfaces;
using Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace Web.Workers.Reapers;

/// <summary>
/// Fails scans that have been Running or Queued for longer than their respective timeout windows.
///
/// Defaults:
///   CheckInterval  — 10 minutes
///   Running        — 1 hour    (worker picked it up but never completed)
///   Queued         — 5 minutes (worker never picked it up, e.g. Redis was down)
///
/// Overridable with ScanReaper:CheckInterval, ScanReaper:RunningTimeout and
/// ScanReaper:QueuedTimeout, as TimeSpan strings such as "00:10:00".
/// </summary>
/// <remarks>
/// This only marks database rows. It does not, and cannot cheaply, remove the matching entry from
/// the Redis scan-jobs queue — Redis lists have no random-access delete. That asymmetry is why a
/// backlog once survived six weeks after the API had already failed every one of those scans: the
/// worker kept its own copy. The queue side is handled where it belongs, by the staleness check in
/// the worker's QueueListener, which drops jobs older than its own cutoff on dequeue.
/// </remarks>
public class ScanReaperWorker : BackgroundService
{
    private readonly ILogger<ScanReaperWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly TimeSpan _checkInterval;
    private readonly TimeSpan _runningTimeout;
    private readonly TimeSpan _queuedTimeout;

    public ScanReaperWorker(
        ILogger<ScanReaperWorker> logger,
        IServiceScopeFactory scopeFactory,
        IConfiguration config)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;

        // Previously these were hardcoded static readonly fields while ScanReaper__* was set in the
        // deployed environment, so anyone tuning those was changing nothing.
        _checkInterval  = Read(config, "ScanReaper:CheckInterval",  TimeSpan.FromMinutes(10));
        _runningTimeout = Read(config, "ScanReaper:RunningTimeout", TimeSpan.FromHours(1));
        _queuedTimeout  = Read(config, "ScanReaper:QueuedTimeout",  TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Falls back to the default on anything unusable, including a non-positive interval: a zero
    /// CheckInterval would spin this loop against the database as fast as it could run.
    /// </summary>
    private TimeSpan Read(IConfiguration config, string key, TimeSpan fallback)
    {
        var raw = config[key];
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        if (!TimeSpan.TryParse(raw, out var parsed) || parsed <= TimeSpan.Zero)
        {
            _logger.LogWarning(
                "{Key} value '{Raw}' is not a positive TimeSpan; falling back to {Fallback}.",
                key, raw, fallback);
            return fallback;
        }

        return parsed;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "ScanReaperWorker started — every {Interval}, failing Running scans older than {Running} and Queued older than {Queued}",
            _checkInterval, _runningTimeout, _queuedTimeout);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ReapAbandonedScans(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "ScanReaperWorker tick failed");
            }

            await Task.Delay(_checkInterval, ct);
        }
    }

    private async Task ReapAbandonedScans(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IVulnWatchDbContext>();

        // Running scans that started more than RunningTimeout ago
        var runningCutoff = DateTime.UtcNow - _runningTimeout;
        var stalledRunning = await context.Scans
            .Where(s => s.Status == ScanStatus.Running
                     && (s.StartedAt == null || s.StartedAt < runningCutoff))
            .ToListAsync(ct);

        // Queued scans created more than QueuedTimeout ago (worker never picked them up)
        var queuedCutoff = DateTime.UtcNow - _queuedTimeout;
        var abandonedQueued = await context.Scans
            .Where(s => s.Status == ScanStatus.Queued
                     && s.CreatedAt < queuedCutoff)
            .ToListAsync(ct);

        var total = stalledRunning.Count + abandonedQueued.Count;

        if (total == 0)
        {
            _logger.LogDebug("ScanReaperWorker tick — no abandoned scans found");
            return;
        }

        foreach (var scan in stalledRunning)
        {
            scan.Fail();
            _logger.LogWarning(
                "Reaped stalled Running scan {ScanId} — started at {StartedAt:u}, exceeded {Timeout} timeout",
                scan.Id, scan.StartedAt, _runningTimeout);
        }

        foreach (var scan in abandonedQueued)
        {
            scan.Fail();
            _logger.LogWarning(
                "Reaped abandoned Queued scan {ScanId} — created at {CreatedAt:u}, exceeded {Timeout} timeout",
                scan.Id, scan.CreatedAt, _queuedTimeout);
        }

        await context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "ScanReaperWorker reaped {Running} stalled running + {Queued} abandoned queued scan(s)",
            stalledRunning.Count, abandonedQueued.Count);
    }
}
