using Application.Features.Scans.DTOs;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Web.Workers.Monitoring;

public sealed class ScanDispatchService(
    IScanRepository scanRepo,
    IRedisService redis,
    IScanJobFactory scanJobFactory,
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
            // Dns | Ssl | Http used to collapse to 3, since Http was 3 and already held both other
            // bits. The payload never reached the worker either, so scheduled scans ran every
            // scanner regardless of this list. Now that the mask decomposes and is actually sent,
            // this list is what runs - so it names every domain surface the worker implements,
            // preserving today's behaviour rather than silently narrowing it. Dependency and
            // Secrets are omitted: no domain scanner implements them.
            surfaceTypes: SurfaceType.Dns | SurfaceType.Ssl | SurfaceType.HttpHeaders
                          | SurfaceType.Subdomains | SurfaceType.Ports,
            domainId: domainId);

        await scanRepo.AddAsync(scan, ct);
        await scanRepo.SaveChangesAsync(ct);

        try
        {
            var scanJob = scanJobFactory.Create(scan);

            await redis.PublishScanJob("scan-jobs", scanJob, ct);
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

        // logger.LogInformation(
        //     "Monitoring scan dispatched for {Domain} — scan {ScanId}",
        //     domainName, scan.Id);

        return true;
    }
}