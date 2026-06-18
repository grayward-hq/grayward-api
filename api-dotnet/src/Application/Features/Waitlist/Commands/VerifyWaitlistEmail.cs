using Application.Features.Auth.DTOs;
using Application.Features.Waitlist.DTOs;
using Application.Interfaces;
using Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Features.Waitlist.Commands;

public record VerifyWaitlistEmailCommand(string Email, string Token) 
    : IRequest<Result<MessageResponse>>;

public class VerifyWaitlistEmailHandler : IRequestHandler<VerifyWaitlistEmailCommand, Result<MessageResponse>>
{
    private readonly IWaitlistRepository _waitlistRepo;
    private readonly ILogger<VerifyWaitlistEmailHandler> _logger;

    public VerifyWaitlistEmailHandler(
        IWaitlistRepository waitlistRepo,
        ILogger<VerifyWaitlistEmailHandler> logger)
    {
        _waitlistRepo = waitlistRepo;
        _logger = logger;
    }

    public async Task<Result<MessageResponse>> Handle(VerifyWaitlistEmailCommand cmd, CancellationToken ct)
    {
        var entry = await _waitlistRepo.FindByEmail(cmd.Email.ToLower(), ct);
        
        if (entry is null)
        {
            _logger.LogWarning("Email verification attempted for non-existent email: {email}", cmd.Email);
            return Result<MessageResponse>.Failure(
                Error.NotFound("Email not found on waitlist"));
        }

        if (entry.EmailConfirmed)
        {
            _logger.LogInformation("Email verification attempted for already confirmed email: {email}", cmd.Email);
            return Result<MessageResponse>.Failure(
                Error.Conflict("Email already confirmed"));
        }

        if (!entry.ValidateEmailConfirmationToken(cmd.Token))
        {
            _logger.LogWarning("Invalid or expired token for email: {email}", cmd.Email);
            return Result<MessageResponse>.Failure(
                Error.Validation("Invalid or expired verification token"));
        }

        entry.ConfirmEmail();
        _waitlistRepo.Update(entry);
        await _waitlistRepo.SaveChangesAsync(ct);

        _logger.LogInformation("Email confirmed for: {email}", cmd.Email);

        return Result<MessageResponse>.Success(
            MessageResponse.Create("Email successfully verified"));
    }
}
