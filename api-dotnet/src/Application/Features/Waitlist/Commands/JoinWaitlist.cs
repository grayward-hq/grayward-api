using Application.Features.Waitlist.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Application.Features.Waitlist.Commands;

using WaitlistEntity = global::Domain.Entities.Waitlist;

public record JoinWaitlistCommand(
    string Email,
    string? CompanyName = null,
    string? Comments = null,
    string? ReferralCode = null)
    : IRequest<Result<WaitlistResponse>>;

public class JoinWaitlistHandler : IRequestHandler<JoinWaitlistCommand, Result<WaitlistResponse>>
{
    private readonly IWaitlistRepository _waitlistRepo;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly IWaitlistCancellationTokenService _cancellationTokenService;
    private readonly UserManager<User> _userManager;
    private readonly IRedisService _redis;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<JoinWaitlistHandler> _logger;

    public JoinWaitlistHandler(
        IWaitlistRepository waitlistRepo,
        IEmailService emailService,
        IConfiguration config,
        IWaitlistCancellationTokenService cancellationTokenService,
        UserManager<User> userManager,
        IRedisService redis,
        IHttpContextAccessor http,
        ILogger<JoinWaitlistHandler> logger)
    {
        _waitlistRepo = waitlistRepo;
        _emailService = emailService;
        _config = config;
        _cancellationTokenService = cancellationTokenService;
        _userManager = userManager;
        _redis = redis;
        _http = http;
        _logger = logger;
    }



    public async Task<Result<WaitlistResponse>> Handle(JoinWaitlistCommand cmd, CancellationToken ct)
    {
        var normalizedEmail = cmd.Email.Trim().ToLowerInvariant();
        var normalizedReferralCode = NormalizeReferralCode(cmd.ReferralCode);

        var genericSuccessResponse = new WaitlistResponse(
            normalizedEmail,
            Position: 0, 
            Status: WaitlistStatus.Pending, 
            CreatedAt: DateTime.UtcNow,
            EmailConfirmed: false
        );

        // An existing entry that is NOT cancelled means the email is already on the waitlist
        // (pending/confirmed/promoted) — mask it to avoid enumeration.
        var existingWaitlistEntry = await _waitlistRepo.FindByEmail(normalizedEmail, ct);
        if (existingWaitlistEntry is not null && existingWaitlistEntry.Status != WaitlistStatus.Cancelled)
        {
            _logger.LogInformation("Account enumeration masked: attempt to join waitlist for existing entry {waitlistEntryId}.", existingWaitlistEntry.Id);

            // Let the real address owner know they're already on the list (and where they stand). The
            // API response stays generic, so this reveals nothing to a form-submitting attacker — the
            // mail only reaches the mailbox owner. Best-effort: a send failure must not change the
            // masked response.
            await SendAlreadyJoinedEmail(existingWaitlistEntry, ct);

            return Result<WaitlistResponse>.Success(genericSuccessResponse);
        }

        // Check if email already registered as user
        var existingUser = await _userManager.FindByEmailAsync(normalizedEmail);
        if (existingUser is not null)
        {
            _logger.LogInformation("Account enumeration masked: attempt to join waitlist for already-registered user {userId}.", existingUser.Id);

            // Same masking rationale as the already-on-waitlist branch: notify the real address owner
            // (who already has an account) without revealing anything to a form-submitting attacker.
            await SendAlreadyRegisteredEmail(normalizedEmail, existingUser.Id, ct);

            return Result<WaitlistResponse>.Success(genericSuccessResponse);
        }

        WaitlistEntity? referrer = null;
        if (!string.IsNullOrWhiteSpace(normalizedReferralCode))
        {
            referrer = await _waitlistRepo.FindByReferralCode(normalizedReferralCode, ct);
            if (referrer?.Status is WaitlistStatus.Cancelled or WaitlistStatus.Promoted)
            {
                referrer = null;
            }
        }

        // A previously cancelled entry rejoins by reactivating in place; a new email is created.
        // Position and referral code are NOT assigned here — they are claimed on email confirmation.
        var isReactivation = existingWaitlistEntry is not null;
        var entry = existingWaitlistEntry ?? WaitlistEntity.Create(
            normalizedEmail,
            cmd.CompanyName,
            cmd.Comments,
            referrer?.Id);

        if (isReactivation)
        {
            entry.UpdateCompanyName(cmd.CompanyName);
            entry.UpdateComments(cmd.Comments);
            entry.Reactivate(referrer?.Id);
        }

        var confirmationToken = WaitlistTokens.NewConfirmationToken();
        entry.GenerateEmailConfirmationToken(confirmationToken);

        // Capture the allowlisted frontend origin this join came from, so later links built without a
        // request Origin header (e.g. the referral link at confirmation time) can route to the same
        // environment. Only ever an allowlisted value; null when the origin is unknown/not allowed.
        entry.SetJoinOrigin(WaitlistLinks.ResolveAllowedOrigin(_config, _http.HttpContext?.Request));

        try
        {
            if (isReactivation)
                _waitlistRepo.Update(entry);
            else
                await _waitlistRepo.AddAsync(entry, ct);

            await _waitlistRepo.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Most likely a concurrent join with the same email — mask it.
            _logger.LogInformation(
                ex,
                "Waitlist join hit unique constraint {constraintName} for entry {waitlistEntryId}.",
                GetConstraintName(ex),
                entry.Id);
            return Result<WaitlistResponse>.Success(genericSuccessResponse);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to save waitlist entry {waitlistEntryId}.", entry.Id);
            return Result<WaitlistResponse>.Failure(
                Error.Validation("Could not join waitlist. Please try again."));
        }

        try
        {
            var request = _http.HttpContext?.Request;
            var confirmLink = WaitlistLinks.BuildConfirmationLink(_config, request, normalizedEmail, confirmationToken);
            var cancellationToken = _cancellationTokenService.GenerateToken(entry.Id, normalizedEmail);
            var cancellationLink = WaitlistLinks.BuildCancellationLink(_config, request, normalizedEmail, cancellationToken);

            await SendConfirmationEmail(normalizedEmail, confirmLink, cancellationLink);
            _logger.LogInformation("Confirmation email sent successfully for entry {waitlistEntryId}", entry.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send confirmation email for entry {waitlistEntryId}. Aborting registration.", entry.Id);

            // Roll back: a new entry is removed; a reactivated one is returned to cancelled.
            if (isReactivation)
            {
                entry.MarkCancelled();
                _waitlistRepo.Update(entry);
            }
            else
            {
                _waitlistRepo.Remove(entry);
            }

            // Not ct: the rollback must persist even when the caller has disconnected and
            // cancelled the request, otherwise the entry is stranded with no email sent.
            await _waitlistRepo.SaveChangesAsync(CancellationToken.None);

            return Result<WaitlistResponse>.Failure(
                Error.Validation("Could not send confirmation email. Please verify your address and try again."));
        }

        // Position and referral code are claimed on email confirmation, so the join response is
        // the generic pending response — the user must confirm their email to secure a spot.
        return Result<WaitlistResponse>.Success(genericSuccessResponse);
    }



    private async Task SendConfirmationEmail(
        string email,
        string confirmLink,
        string cancellationLink)
    {
        var body = WaitlistConfirmationEmail.BuildBody(confirmLink, cancellationLink);
        await _emailService.SendAsync(email, WaitlistConfirmationEmail.Subject, body);
    }

    private async Task SendAlreadyJoinedEmail(WaitlistEntity entry, CancellationToken ct)
    {
        try
        {
            // Throttle: this notice is triggered by whoever submits the address, so cap it per
            // recipient to prevent inbox flooding. Skip silently when a recent send holds the slot.
            if (!await _redis.TryClaimEmailCooldownSlot(
                    WaitlistEmailThrottle.Purpose, entry.Email, WaitlistEmailThrottle.Cooldown(_config), ct))
            {
                _logger.LogInformation("Already-on-waitlist notice throttled for existing entry {waitlistEntryId}", entry.Id);
                return;
            }

            // Position is only meaningful for a confirmed entry — the live rank among active
            // entries (so cancellations shift everyone up). Pending/promoted entries have none.
            long? livePosition = null;
            if (entry.Status == WaitlistStatus.EmailConfirmed && entry.Position is long sequence)
            {
                livePosition = await _waitlistRepo.GetLivePosition(sequence, ct);
            }

            var body = WaitlistAlreadyJoinedEmail.BuildBody(entry.Status, livePosition);
            await _emailService.SendAsync(entry.Email, WaitlistAlreadyJoinedEmail.Subject, body);
            _logger.LogInformation("Already-on-waitlist notice sent to existing entry {waitlistEntryId}", entry.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send already-on-waitlist notice to entry {waitlistEntryId}.", entry.Id);
        }
    }

    private async Task SendAlreadyRegisteredEmail(string email, Guid userId, CancellationToken ct)
    {
        try
        {
            // Same per-recipient throttle as the already-on-waitlist notice.
            if (!await _redis.TryClaimEmailCooldownSlot(
                    WaitlistEmailThrottle.Purpose, email, WaitlistEmailThrottle.Cooldown(_config), ct))
            {
                _logger.LogInformation("Already-registered notice throttled for user {userId}", userId);
                return;
            }

            var body = WaitlistAlreadyRegisteredEmail.BuildBody();
            await _emailService.SendAsync(email, WaitlistAlreadyRegisteredEmail.Subject, body);
            _logger.LogInformation("Already-registered notice sent to user {userId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send already-registered notice to user {userId}.", userId);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner?.GetType().FullName != "Npgsql.PostgresException") return false;

        return inner.GetType()
            .GetProperty("SqlState")?
            .GetValue(inner) as string == "23505";
    }

    private static string? GetConstraintName(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner?.GetType().FullName != "Npgsql.PostgresException") return null;

        return inner.GetType()
            .GetProperty("ConstraintName")?
            .GetValue(inner) as string;
    }

    private static string? NormalizeReferralCode(string? referralCode)
        => string.IsNullOrWhiteSpace(referralCode)
            ? null
            : referralCode.Trim().ToUpperInvariant();
}
