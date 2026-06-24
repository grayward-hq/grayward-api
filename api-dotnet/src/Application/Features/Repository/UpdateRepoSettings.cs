using Application.Features.Auth.DTOs;
using Application.Helpers;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Application.Features.Repository;

public record UpdateRepoSettingsCommand(
    Guid RepositoryId,
    bool PeriodicScanEnabled,
    ScanFrequency PeriodicScanFrequency,
    bool EventScanEnabled,
    RepositoryEventTrigger Triggers,
    AlertChannel AlertChannels,
    string Version) : IRequest<Result<MessageResponse>>;

public class UpdateRepoSettingsValidator : AbstractValidator<UpdateRepoSettingsCommand>
{
    public UpdateRepoSettingsValidator()
    {
        RuleFor(x => x.RepositoryId).NotEmpty();

        RuleFor(x => x.PeriodicScanFrequency)
            .IsInEnum().WithMessage("Unknown scan frequency.");

        RuleFor(x => x.Triggers)
            .NotEqual(RepositoryEventTrigger.None)
            .When(x => x.EventScanEnabled)
            .WithMessage("Select at least one event trigger when event scanning is enabled.");

        RuleFor(x => x.Version)
            .Must(v => uint.TryParse(v, out _))
            .WithMessage("Missing or invalid concurrency token; reload and try again.");
    }

    private static bool BeValidBase64(string value)
    {
        Span<byte> buffer = stackalloc byte[256];
        return Convert.TryFromBase64String(value, buffer, out _);
    }
}


public class UpdateRepoSettingsHandler(
    IVulnWatchDbContext db,
    IMonitoredRepoRepository repos,
    ICurrentUser currentUser,
    TimeProvider clock)
    : IRequestHandler<UpdateRepoSettingsCommand, Result<MessageResponse>>
{
    public async Task<Result<MessageResponse>> Handle(UpdateRepoSettingsCommand cmd, CancellationToken ct)
    {
        var userId = currentUser.UserId;

        var repo = await repos.GetUserRepoByRepoId(userId, cmd.RepositoryId, ct);

        if (repo is null)
            return Result<MessageResponse>.Failure(Error.NotFound("Repository not found."));

        var wasBackfilled = repo.EnsureSettings();

        if (!wasBackfilled && uint.TryParse(cmd.Version, out var originalVersion))
            db.SetOriginalVersion(repo.Settings, originalVersion);

        try
        {
            repo.Settings.Configure(
                cmd.PeriodicScanEnabled, cmd.PeriodicScanFrequency,
                cmd.EventScanEnabled, cmd.Triggers,
                cmd.AlertChannels, clock.GetUtcNow().UtcDateTime);

            await db.SaveChangesAsync(ct);
        }
        catch (DomainException ex)
        {
            return Result<MessageResponse>.Failure(Error.Validation(ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<MessageResponse>.Failure(
                Error.Conflict("These settings were changed elsewhere. Reload and try again."));
        }

        return Result<MessageResponse>.Success(MessageResponse.Create("Repository settings updated."));
    }
}