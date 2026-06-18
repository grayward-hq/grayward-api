using Application.Features.Auth.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Features.Waitlist.Commands;

public record CancelWaitlistCommand(string Email) 
    : IRequest<Result<MessageResponse>>;

public class CancelWaitlistHandler : IRequestHandler<CancelWaitlistCommand, Result<MessageResponse>>
{
    private readonly IWaitlistRepository _waitlistRepo;
    private readonly ILogger<CancelWaitlistHandler> _logger;

    public CancelWaitlistHandler(
        IWaitlistRepository waitlistRepo,
        ILogger<CancelWaitlistHandler> logger)
    {
        _waitlistRepo = waitlistRepo;
        _logger = logger;
    }

    public async Task<Result<MessageResponse>> Handle(CancelWaitlistCommand cmd, CancellationToken ct)
    {
        var entry = await _waitlistRepo.FindByEmail(cmd.Email.ToLower(), ct);
        
        if (entry is null)
        {
            _logger.LogWarning("Cancellation attempted for non-existent email: {email}", cmd.Email);
            return Result<MessageResponse>.Failure(
                Error.NotFound("Email not found on waitlist"));
        }

        if (entry.Status == WaitlistStatus.Cancelled)
        {
            _logger.LogInformation("Cancellation attempted for already cancelled entry: {email}", cmd.Email);
            return Result<MessageResponse>.Failure(
                Error.Conflict("Entry already cancelled"));
        }

        if (entry.Status == WaitlistStatus.Promoted)
        {
            _logger.LogInformation("Cancellation attempted for promoted entry: {email}", cmd.Email);
            return Result<MessageResponse>.Failure(
                Error.Conflict("Cannot cancel - user account already created"));
        }

        entry.MarkCancelled();
        _waitlistRepo.Update(entry);
        await _waitlistRepo.SaveChangesAsync(ct);

        _logger.LogInformation("Waitlist entry cancelled for: {email}", cmd.Email);

        return Result<MessageResponse>.Success(
            MessageResponse.Create("Successfully removed from waitlist"));
    }
}
