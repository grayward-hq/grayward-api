using Application.Features.Auth.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Features.Waitlist.Commands;

/// <summary>
/// Cancels a waitlist entry. <paramref name="Token"/> is a signed,
/// time-limited token proving the caller has access to the email's
/// inbox (it is delivered via the cancellation link sent to that email).
/// Without a valid token, the entry cannot be cancelled — knowledge of
/// the email address alone is not sufficient.
/// </summary>
public record CancelWaitlistCommand(string Email, string Token)
    : IRequest<Result<MessageResponse>>;

public class CancelWaitlistHandler : IRequestHandler<CancelWaitlistCommand, Result<MessageResponse>>
{
    private readonly IWaitlistRepository _waitlistRepo;
    private readonly IWaitlistCancellationTokenService _tokenService;
    private readonly ILogger<CancelWaitlistHandler> _logger;

    public CancelWaitlistHandler(
        IWaitlistRepository waitlistRepo,
        IWaitlistCancellationTokenService tokenService,
        ILogger<CancelWaitlistHandler> logger)
    {
        _waitlistRepo = waitlistRepo;
        _tokenService = tokenService;
        _logger = logger;
    }

    public async Task<Result<MessageResponse>> Handle(CancelWaitlistCommand cmd, CancellationToken ct)
    {
        var normalizedEmail = cmd.Email.ToLowerInvariant();
        var entry = await _waitlistRepo.FindByEmail(normalizedEmail, ct);

        if (entry is null)
        {
            _logger.LogWarning("Cancellation attempted for non-existent email: {email}", cmd.Email);
            return Result<MessageResponse>.Failure(
                Error.NotFound("Email not found on waitlist"));
        }

        // Proof-of-possession check: the caller must present a signed token
        // bound to this specific entry and email. This is what prevents an
        // attacker who merely knows the email address from cancelling
        // someone else's entry.
        if (!_tokenService.ValidateToken(cmd.Token, entry.Id, normalizedEmail))
        {
            _logger.LogWarning(
                "Cancellation attempted with invalid/expired token for: {email}", cmd.Email);
            return Result<MessageResponse>.Failure(
                Error.Unauthorized("Invalid or expired cancellation token"));
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
