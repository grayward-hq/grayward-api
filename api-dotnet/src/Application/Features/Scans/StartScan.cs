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

public record StartScanCommand(string Domain, ScanCoverage Coverage, SurfaceType SurfaceTypes, Guid IdempotencyKey) : IRequest<Result<StartScanResponse>>;

public record StartScanRequest(string Domain, SurfaceType SurfaceTypes, ScanCoverage Coverage);

public record StartScanResponse(Guid ScanId, ScanStatus Status, string Message);

public class StartScanCommandValidator : AbstractValidator<StartScanCommand>
{
    public StartScanCommandValidator()
    {
        RuleFor(x => x.Domain)
            .NotEmpty()
            .MaximumLength(253)
            .Matches(@"^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.[A-Za-z]{2,})+$")
            .WithMessage("Invalid domain format.");

        RuleFor(x => x.IdempotencyKey)
            .NotEqual(Guid.Empty)
            .WithMessage("A valid idempotency key is required.");

        RuleFor(x => x.Coverage)
            .IsInEnum();
    }
}
public class StartScanHandler : IRequestHandler<StartScanCommand, Result<StartScanResponse>>
{
    private readonly IVulnWatchDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IScanRepository _scanRepo;
    private readonly IDomainRepository _domainRepo;
    private readonly IRedisService _redis;
    private readonly ILogger<StartScanHandler> _logger;
    private readonly string _scanJobsQueue;

    public StartScanHandler(IVulnWatchDbContext context, ICurrentUser currentUser, IScanRepository scanRepo, IDomainRepository domainRepo, IRedisService redis, ILogger<StartScanHandler> logger, IConfiguration config)
    {
        _context = context;
        _currentUser = currentUser;
        _scanRepo = scanRepo;
        _domainRepo = domainRepo;
        _redis = redis;
        _logger = logger;
        _scanJobsQueue = config["Worker:ScanJobsQueue"] ?? "scan-jobs";
    }

    public async Task<Result<StartScanResponse>> Handle(StartScanCommand cmd, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        var domain = await _domainRepo.FindUserDomainByName(userId, cmd.Domain, ct);

        if (domain is null)
            return Result<StartScanResponse>.Failure(Error.NotFound("Domain not found."));

        if (domain.VerificationStatus != VerificationStatus.Verified)
            return Result<StartScanResponse>.Failure(Error.Forbidden("Domain ownership unverified! Verify before initiating a scan."));

        // Idempotency check — same request retried or double-submitted
        var existingByKey = await _scanRepo.FindByIdempotencyKey(cmd.IdempotencyKey, ct);
        if (existingByKey is not null)
            return Result<StartScanResponse>.Success(
                new StartScanResponse(existingByKey.Id, existingByKey.Status, "Scan already initiated."));

        await using var tx = await _context.Database.BeginTransactionAsync(ct);

        try
        {
            // Concurrency check — different request but scan already running on this domain
            var running = await _scanRepo.FindRunningByDomain(domain.Id, ct);
            if (running is not null)
            {
                await tx.RollbackAsync(ct);
                return Result<StartScanResponse>.Success(
                    new StartScanResponse(running.Id, running.Status, "A scan is already in progress for this domain."));
            }

            var scan = Scan.Create(
                userId,
                idempotencyKey: cmd.IdempotencyKey,
                ScanTargetType.Domain,
                cmd.Coverage,
                cmd.SurfaceTypes,
                domain.Id,
                null
            );

            await _scanRepo.AddAsync(scan, ct);
            await _scanRepo.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            // Publish after commit — worker only sees jobs backed by a committed row
            await _redis.PublishScanJob(_scanJobsQueue, new ScanJob(
                domain.Id, domain.DomainName, scan.Id,
                ScanTargetType.Domain.ToString(), scan.SurfaceTypes.ToString(),  userId, scan.CreatedAt), ct);

            _logger.LogInformation("Scan {ScanId} queued for domain {DomainId}", scan.Id, domain.Id);

            return Result<StartScanResponse>.Success(
                new StartScanResponse(scan.Id, scan.Status, "Scan started successfully."));
        }
        catch (Exception ex)
        {
            // await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to start scan for domain {DomainId}", domain.Id);
            return Result<StartScanResponse>.Failure(Error.Internal("Failed to start scan. Please try again."));
        }
    }
}
