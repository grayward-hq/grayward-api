using Application.Features.Waitlist.DTOs;
using Application.Interfaces;
using Domain.Common;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Application.Features.Waitlist.Commands;

public record VerifyWaitlistEmailCommand(string Email, string Token)
    : IRequest<Result<WaitlistResponse>>;

public class VerifyWaitlistEmailHandler : IRequestHandler<VerifyWaitlistEmailCommand, Result<WaitlistResponse>>
{
    private readonly IWaitlistRepository _waitlistRepo;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<VerifyWaitlistEmailHandler> _logger;

    public VerifyWaitlistEmailHandler(
        IWaitlistRepository waitlistRepo,
        IEmailService emailService,
        IConfiguration config,
        ILogger<VerifyWaitlistEmailHandler> logger)
    {
        _waitlistRepo = waitlistRepo;
        _emailService = emailService;
        _config = config;
        _logger = logger;
    }

    public async Task<Result<WaitlistResponse>> Handle(VerifyWaitlistEmailCommand cmd, CancellationToken ct)
    {
        var entry = await _waitlistRepo.FindByEmail(cmd.Email.ToLowerInvariant(), ct);

        if (entry is null)
        {
            _logger.LogWarning("Email verification attempted for non-existent email: {email}", cmd.Email);
            return Result<WaitlistResponse>.Failure(
                Error.NotFound("Email not found on waitlist"));
        }

        if (entry.EmailConfirmed)
        {
            _logger.LogInformation("Email verification attempted for already confirmed email: {email}", cmd.Email);
            return Result<WaitlistResponse>.Failure(
                Error.Conflict("Email already confirmed"));
        }

        if (!entry.ValidateEmailConfirmationToken(cmd.Token))
        {
            _logger.LogWarning("Invalid or expired token for email: {email}", cmd.Email);
            return Result<WaitlistResponse>.Failure(
                Error.Validation("Invalid or expired verification token"));
        }

        // Claim the queue slot and referral code now that the email is confirmed.
        var sequence = await _waitlistRepo.GetNextPosition(ct);
        var referralCode = await GenerateUniqueReferralCode(ct);
        entry.ConfirmEmail(sequence, referralCode);
        _waitlistRepo.Update(entry);
        await _waitlistRepo.SaveChangesAsync(ct);

        // Credit the referrer only now that this is a confirmed signup. A failure here must not
        // fail the user's own confirmation, so it is best-effort and logged.
        if (entry.ReferredByWaitlistId is Guid referrerId)
        {
            try
            {
                var bumped = await _waitlistRepo.ApplyReferralBump(referrerId, ct);
                _logger.LogInformation(
                    "Referral credit for referrer {referrerId} after confirmation by {waitlistEntryId}: applied={bumped}",
                    referrerId, entry.Id, bumped);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to apply referral credit for referrer {referrerId} after confirmation by {waitlistEntryId}",
                    referrerId, entry.Id);
            }
        }

        var livePosition = await _waitlistRepo.GetLivePosition(sequence, ct);
        var referralLink = WaitlistLinks.BuildReferralLink(_config, entry.ReferralCode!);

        // Post-confirmation email with the claimed position and referral link. Best-effort:
        // a mail failure must not fail the confirmation (the data is also in the API response).
        try
        {
            await SendConfirmedEmail(entry.Email, livePosition, referralLink);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send post-confirmation email to {waitlistEntryId}", entry.Id);
        }

        _logger.LogInformation("Email confirmed for: {email}", cmd.Email);

        return Result<WaitlistResponse>.Success(
            new WaitlistResponse(
                entry.Email,
                livePosition,
                entry.Status,
                entry.CreatedAt,
                entry.EmailConfirmed,
                EmailConfirmedAt: entry.EmailConfirmedAt,
                ReferralCode: entry.ReferralCode,
                ReferralLink: referralLink));
    }

    private async Task SendConfirmedEmail(string email, long position, string referralLink)
    {
        var body = BuildConfirmedEmailBody(position, referralLink);
        await _emailService.SendAsync(email, "You're on the Vulnwatch waitlist! 🎯", body);
    }

    private static string BuildConfirmedEmailBody(long position, string referralLink)
    {
        return $@"
    <!DOCTYPE html>
    <html>
    <head>
        <meta charset='UTF-8'>
        <title>You're on the waitlist</title>
    </head>
    <body style='font-family: Arial, sans-serif; background-color: #f9f9f9; padding: 20px;'>
        <div style='max-width: 600px; margin: auto; background: #ffffff; padding: 30px; border-radius: 8px;'>
            <h2 style='color: #333;'>You're in! 🎉</h2>

            <p style='font-size: 16px; color: #555;'>
                Your email is confirmed and your spot is secured. You're currently
                <strong>#{position}</strong> on the Vulnwatch waitlist.
            </p>

            <p style='font-size: 14px; color: #555; margin-top: 24px;'>
                Want to move up the queue? Share your referral link — every person who joins with it
                moves you closer to the front:
            </p>

            <p style='font-size: 14px; color: #999;'>
                <code style='background-color: #f0f0f0; padding: 5px; display: inline-block;'>{referralLink}</code>
            </p>
        </div>
    </body>
    </html>";
    }

    private async Task<string> GenerateUniqueReferralCode(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = GenerateReferralCode();
            if (await _waitlistRepo.FindByReferralCode(code, ct) is null)
            {
                return code;
            }
        }

        return Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    }

    private static string GenerateReferralCode()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes)[..10];
    }

}
