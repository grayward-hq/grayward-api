using Application.Features.Scans.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Web.Workers.Monitoring;

public sealed class ScanDispatchService(
    IScanRepository scanRepo,
    IRedisService redis,
    ILogger<ScanDispatchService> logger)
{
    public async Task<bool> DispatchAsync(
        DomainSettings settings,
        CancellationToken ct)
    {
        var domainId = settings.DomainId;
        var domainName = settings.Domain.DomainName;

        var running = await scanRepo.FindRunningByDomain(domainId, ct);
        if (running is not null)
        {
            logger.LogDebug(
                "Skipping dispatch for {Domain} — scan {ScanId} already running",
                domainName, running.Id);
            return false;
        }

        var idempotencyKey = Guid.NewGuid();

        var scan = Domain.Entities.Scan.Create(
            userId: settings.Domain.UserId,
            idempotencyKey: idempotencyKey,
            targetType: ScanTargetType.Domain,
            coverage: ScanCoverage.Full,
            surfaceTypes: SurfaceType.Dns | SurfaceType.Ssl | SurfaceType.Http,
            domainId: domainId);

        await scanRepo.AddAsync(scan, ct);
        await scanRepo.SaveChangesAsync(ct);

        try
        {
            await redis.PublishScanJob("scan-jobs", new ScanJob(
                domainId,
                domainName,
                scan.Id,
                ScanTargetType.Domain.ToString(),
                scan.SurfaceTypes.ToString(),
                settings.Domain.UserId,
                scan.CreatedAt), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to publish scan job for {Domain} (scan {ScanId}) — marking as Failed",
                domainName, scan.Id);

            scan.Fail();

            try
            {
                await scanRepo.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception compensationEx)
            {
                logger.LogError(compensationEx,
                    "Compensation save also failed for scan {ScanId} — record may be stuck",
                    scan.Id);
            }

            return false;
        }

        logger.LogInformation(
            "Monitoring scan dispatched for {Domain} — scan {ScanId}",
            domainName, scan.Id);

        return true;
    }
}