using Application.Features.Waitlist.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Features.Waitlist.Commands;

public record UpdateWaitlistCommand(
    Guid WaitlistId,
    string? CompanyName = null,
    string? Notes = null,
    WaitlistStatus? Status = null)
    : IRequest<Result<WaitlistListItemDto>>;

public class UpdateWaitlistHandler : IRequestHandler<UpdateWaitlistCommand, Result<WaitlistListItemDto>>
{
    private readonly IWaitlistRepository _waitlistRepo;
    private readonly ILogger<UpdateWaitlistHandler> _logger;

    public UpdateWaitlistHandler(
        IWaitlistRepository waitlistRepo,
        ILogger<UpdateWaitlistHandler> logger)
    {
        _waitlistRepo = waitlistRepo;
        _logger = logger;
    }

    public async Task<Result<WaitlistListItemDto>> Handle(UpdateWaitlistCommand cmd, CancellationToken ct)
    {
        var entry = await _waitlistRepo.GetById(cmd.WaitlistId, ct);
        if (entry is null)
        {
            return Result<WaitlistListItemDto>.Failure(
                Error.NotFound("Waitlist entry not found"));
        }

        // Update company name if provided
        if (!string.IsNullOrWhiteSpace(cmd.CompanyName))
        {
            entry.UpdateCompanyName(cmd.CompanyName);
        }

        // Update notes if provided
        if (!string.IsNullOrWhiteSpace(cmd.Notes))
        {
            entry.UpdateNotes(cmd.Notes);
        }

        // Update status if provided (with validation)
        if (cmd.Status.HasValue)
        {
            if (entry.Status == WaitlistStatus.Promoted || entry.Status == WaitlistStatus.Cancelled)
            {
                _logger.LogWarning("Attempted to change status of {email} from {oldStatus} to {newStatus}", 
                    entry.Email, entry.Status, cmd.Status);
                return Result<WaitlistListItemDto>.Failure(
                    Error.Validation("Cannot change status of promoted or cancelled entries"));
            }

            // Allow transitions
            if (cmd.Status == WaitlistStatus.Cancelled)
            {
                entry.MarkCancelled();
            }
        }

        _waitlistRepo.Update(entry);
        await _waitlistRepo.SaveChangesAsync(ct);

        _logger.LogInformation("Waitlist entry updated: {email}", entry.Email);

        return Result<WaitlistListItemDto>.Success(
            new WaitlistListItemDto(
                entry.Id,
                entry.Email,
                entry.CompanyName,
                entry.Position,
                entry.Status,
                entry.EmailConfirmed,
                entry.CreatedAt,
                entry.EmailConfirmedAt,
                entry.PromotedAt,
                entry.Comments,
                entry.Notes));
    }
}
