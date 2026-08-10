using Application.Features.Waitlist.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
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
    private readonly IHttpContextAccessor _http;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<VerifyWaitlistEmailHandler> _logger;

    public VerifyWaitlistEmailHandler(
        IWaitlistRepository waitlistRepo,
        IEmailService emailService,
        IConfiguration config,
        IHttpContextAccessor http,
        IUnitOfWork uow,
        ILogger<VerifyWaitlistEmailHandler> logger)
    {
        _waitlistRepo = waitlistRepo;
        _emailService = emailService;
        _config = config;
        _http = http;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result<WaitlistResponse>> Handle(VerifyWaitlistEmailCommand cmd, CancellationToken ct)
    {
        var entry = await _waitlistRepo.FindByEmail(cmd.Email.ToLowerInvariant(), ct);

        if (entry is null)
        {
            _logger.LogWarning("Email verification attempted for an email not on the waitlist.");
            return Result<WaitlistResponse>.Failure(
                Error.NotFound("Email not found on waitlist"));
        }

        if (entry.EmailConfirmed)
        {
            _logger.LogInformation("Email verification attempted for already confirmed entry {waitlistEntryId}.", entry.Id);
            return Result<WaitlistResponse>.Failure(
                Error.Conflict("Email already confirmed"));
        }

        if (!entry.ValidateEmailConfirmationToken(cmd.Token))
        {
            _logger.LogWarning("Invalid or expired verification token for entry {waitlistEntryId}.", entry.Id);
            return Result<WaitlistResponse>.Failure(
                Error.Validation("Invalid or expired verification token"));
        }

        // Claim the queue slot and referral code now that the email is confirmed.
        var sequence = await _waitlistRepo.GetNextPosition(ct);
        var referralCode = await GenerateUniqueReferralCode(ct);

        // Confirm the entry and credit the referrer atomically, in one transaction. The referral
        // bump previously ran best-effort AFTER the confirmation committed, so a crash in between
        // permanently lost the credit. Now both commit together or not at all — if anything here
        // fails the whole thing rolls back and the caller retries verification (the token stays
        // valid until a confirmation actually commits), so credit can no longer be silently lost.
        try
        {
            await _uow.InTransaction(async token =>
            {
                entry.ConfirmEmail(sequence, referralCode);
                _waitlistRepo.Update(entry);
                await _waitlistRepo.SaveChangesAsync(token);

                if (entry.ReferredByWaitlistId is Guid referrerId)
                {
                    var bumped = await _waitlistRepo.ApplyReferralBump(referrerId, token);
                    _logger.LogInformation(
                        "Referral credit for referrer {referrerId} after confirmation by {waitlistEntryId}: applied={bumped}",
                        referrerId, entry.Id, bumped);
                }

                return true;
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to atomically confirm entry {waitlistEntryId} and apply referral credit; rolled back.",
                entry.Id);
            return Result<WaitlistResponse>.Failure(
                Error.Validation("Could not complete verification. Please try again."));
        }

        var livePosition = await _waitlistRepo.GetLivePosition(sequence, ct);
        // Prefer the origin captured at join (this /verify request may be a header-less navigation),
        // falling back to the live request origin and then the configured URL — all allowlist-gated.
        var referralLink = WaitlistLinks.BuildReferralLink(
            _config, _http.HttpContext?.Request, entry.ReferralCode!, entry.JoinOrigin);

        // Post-confirmation email with the claimed position and referral link. Best-effort:
        // a mail failure must not fail the confirmation (the data is also in the API response).
        try
        {
            // Denominator for the position card. Counts confirmed entries specifically — the same
            // population GetLivePosition ranks within — so the two numbers agree.
            var totalConfirmed = await _waitlistRepo.CountByStatus(WaitlistStatus.EmailConfirmed, ct);
            await SendConfirmedEmail(entry.Email, livePosition, totalConfirmed, referralLink);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send post-confirmation email to {waitlistEntryId}", entry.Id);
        }

        _logger.LogInformation("Email confirmed for entry {waitlistEntryId}.", entry.Id);

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

    private async Task SendConfirmedEmail(
        string email, long position, int totalConfirmed, string referralLink)
    {
        var body = WaitlistConfirmedEmail.BuildBody(
            WaitlistEmailBranding.From(_config), position, totalConfirmed, referralLink);
        await _emailService.SendAsync(email, WaitlistConfirmedEmail.Subject, body);
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
