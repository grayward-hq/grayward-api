using MediatR;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Data;
using Application.Features.Scans.DTOs;
using FluentValidation;
using Microsoft.Extensions.Configuration;

namespace Application.Features.Scans;

// public record StartScanCommand(string Domain, ScanCoverage Coverage, SurfaceType SurfaceTypes, Guid IdempotencyKey) : IRequest<Result<StartScanResponse>>;
public record StartScanCommand(
    Guid TargetId,
    ScanTargetType TargetType,
    ScanCoverage Coverage,
    SurfaceType SurfaceTypes,
    Guid IdempotencyKey
) : IRequest<Result<StartScanResponse>>;

public record StartScanRequest(Guid Target, ScanTargetType TargetType, ScanCoverage Coverage,
    SurfaceType SurfaceTypes);

public record StartScanResponse(Guid ScanId, ScanStatus Status, string Message);

public class StartScanCommandValidator : AbstractValidator<StartScanCommand>
{
    public StartScanCommandValidator()
    {
        RuleFor(x => x.TargetId)
            .NotEqual(Guid.Empty)
            .WithMessage("A valid domain or repository ID is required.");

        RuleFor(x => x.TargetType)
            .IsInEnum();

        RuleFor(x => x.Coverage)
            .IsInEnum();

        RuleFor(x => x.SurfaceTypes)
            .Must(surfaceTypes => surfaceTypes != 0)
            .WithMessage("At least one surface type must be specified.");

        RuleFor(x => x.IdempotencyKey)
            .NotEqual(Guid.Empty)
            .WithMessage("A valid idempotency key is required.");
    }
}
public class StartScanHandler(
    ICurrentUser currentUser,
    IScanRepository scans,
    IDomainRepository domains,
    IMonitoredRepoRepository repos,
    IRedisService redis,
    IQuotaService quota,
    IScanJobFactory scanJobFactory,
    IConfiguration config,
    ILogger<StartScanHandler> logger)
    : IRequestHandler<StartScanCommand, Result<StartScanResponse>>
{
    private readonly string _scanJobsQueue =
        config["Worker:ScanJobsQueue"] ?? "scan-jobs";

    public async Task<Result<StartScanResponse>> Handle(StartScanCommand cmd, CancellationToken ct)
    {
        var userId = currentUser.UserId;

        var existingByKey = await scans.FindByIdempotencyKey(cmd.IdempotencyKey, ct);
        if (existingByKey is not null)
            return Result<StartScanResponse>.Success(
                new StartScanResponse(existingByKey.Id, existingByKey.Status, "Scan already initiated."));

        Guid? domainId = null;
        Guid? repositoryId = null;

        switch (cmd.TargetType)
        {
            case ScanTargetType.Domain:
                var domain = await domains.FindUserDomainById(userId, cmd.TargetId, ct);

                if (domain is null)
                    return Result<StartScanResponse>.Failure(Error.NotFound("Domain not found."));

                if (domain.VerificationStatus != VerificationStatus.Verified)
                    return Result<StartScanResponse>.Failure(Error.Forbidden("Domain ownership unverified! Verify before initiating a scan."));

                var runningDomainScan = await scans.FindRunningByDomain(domain.Id, ct);
                if (runningDomainScan is not null)
                {
                    return Result<StartScanResponse>.Success(
                        new StartScanResponse(runningDomainScan.Id, runningDomainScan.Status, "A scan is already in progress for this domain."));
                }

                domainId = domain.Id;

                break;

            case ScanTargetType.Repository:
                var repository = await repos.GetUserRepoByRepoId(userId, cmd.TargetId, ct);

                if (repository is null)
                    return Result<StartScanResponse>.Failure(Error.NotFound("Repository not found."));

                if (repository.Status != RepositoryStatus.Active)
                    return Result<StartScanResponse>.Failure(Error.Forbidden("Repository is not active."));

                var runningRepoScan = await scans.FindRunningByRepoid(repository.Id, ct);
                if (runningRepoScan is not null)
                {
                    return Result<StartScanResponse>.Success(
                        new StartScanResponse(runningRepoScan.Id, runningRepoScan.Status, "A scan is already in progress for this domain."));
                }

                repositoryId = repository.Id;

                break;
            
            default:
                return Result<StartScanResponse>.Failure(
                    Error.Validation("Invalid target type."));
        }

        var scan = Scan.Create(
            userId,
            cmd.IdempotencyKey,
            cmd.TargetType,
            cmd.Coverage,
            cmd.SurfaceTypes,
            domainId,
            repositoryId
        );

        var reserved = await quota.ReserveScanSlot(
            userId,
            cmd.TargetType == ScanTargetType.Domain
                ? ResourceKind.Domain
                : ResourceKind.Repository,
            scan, ct);

        if (!reserved.IsSuccess || reserved.Value is null)
            return Result<StartScanResponse>.Failure(reserved.Error!);

        var scanJob = scanJobFactory.Create(reserved.Value);

        await redis.PublishScanJob(_scanJobsQueue, scanJob, ct);

        logger.LogInformation("Scan {ScanId} queued for {Target} {Id}", scan.Id, cmd.TargetType, cmd.TargetId);

        return Result<StartScanResponse>.Success(
            new StartScanResponse(scan.Id, scan.Status, "Scan started successfully."));

    }
}
