using Application.Features.Auth.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Application.Features.Waitlist.Commands;

public record ResendWaitlistConfirmationCommand(string Email)
    : IRequest<Result<MessageResponse>>;

/// <summary>
/// Re-sends the original "confirm your email" waitlist message for a still-pending entry, reusing
/// the same confirmation link that was mailed on join. Used when the user never received (or lost)
/// the first email. Mirrors <see cref="RequestWaitlistCancellationHandler"/>: the response is always
/// a generic success so the endpoint cannot be used to enumerate which emails are on the waitlist.
/// </summary>
public class ResendWaitlistConfirmationHandler
    : IRequestHandler<ResendWaitlistConfirmationCommand, Result<MessageResponse>>
{
    private const string GenericResponseMessage =
        "If this email is on the waitlist and not yet confirmed, a confirmation link has been sent.";

    private readonly IWaitlistRepository _waitlistRepo;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly IWaitlistCancellationTokenService _cancellationTokenService;
    private readonly IRedisService _redis;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<ResendWaitlistConfirmationHandler> _logger;

    public ResendWaitlistConfirmationHandler(
        IWaitlistRepository waitlistRepo,
        IEmailService emailService,
        IConfiguration config,
        IWaitlistCancellationTokenService cancellationTokenService,
        IRedisService redis,
        IHttpContextAccessor http,
        ILogger<ResendWaitlistConfirmationHandler> logger)
    {
        _waitlistRepo = waitlistRepo;
        _emailService = emailService;
        _config = config;
        _cancellationTokenService = cancellationTokenService;
        _redis = redis;
        _http = http;
        _logger = logger;
    }

    public async Task<Result<MessageResponse>> Handle(
        ResendWaitlistConfirmationCommand cmd,
        CancellationToken ct)
    {
        var normalizedEmail = cmd.Email.Trim().ToLowerInvariant();
        var emailHash = HashEmail(normalizedEmail);

        var entry = await _waitlistRepo.FindByEmail(normalizedEmail, ct);
        if (entry is null)
        {
            _logger.LogInformation("Confirmation resend masked for non-existent email");
            return GenericSuccess();
        }

        // Nothing to resend: the spot is either already confirmed, cancelled, or promoted.
        if (entry.EmailConfirmed
            || entry.Status is WaitlistStatus.Cancelled or WaitlistStatus.Promoted)
        {
            _logger.LogInformation(
                "Confirmation resend ignored for [{emailHash}] with status {status} (confirmed={confirmed}).",
                emailHash, entry.Status, entry.EmailConfirmed);
            return GenericSuccess();
        }

        // Throttle before any work: the resend button is the primary inbox-flooding vector, so cap
        // it per recipient. Skip silently (masked response unchanged) when a recent send holds the slot.
        if (!await _redis.TryClaimEmailCooldownSlot(
                WaitlistEmailThrottle.Purpose, normalizedEmail, WaitlistEmailThrottle.Cooldown(_config), ct))
        {
            _logger.LogInformation("Confirmation resend throttled for [{emailHash}]", emailHash);
            return GenericSuccess();
        }

        // Reuse the original confirmation token so the resent link is identical to the first email.
        // A pending entry always has one from join; regenerate defensively only if it is missing.
        var confirmationToken = entry.EmailConfirmationToken;
        if (string.IsNullOrEmpty(confirmationToken))
        {
            confirmationToken = WaitlistTokens.NewConfirmationToken();
            entry.GenerateEmailConfirmationToken(confirmationToken);
            _waitlistRepo.Update(entry);
            await _waitlistRepo.SaveChangesAsync(ct);
        }

        try
        {
            var request = _http.HttpContext?.Request;
            var confirmLink = WaitlistLinks.BuildConfirmationLink(_config, request, normalizedEmail, confirmationToken);
            var cancellationToken = _cancellationTokenService.GenerateToken(entry.Id, normalizedEmail);
            var cancellationLink = WaitlistLinks.BuildCancellationLink(_config, request, normalizedEmail, cancellationToken);

            await SendConfirmationEmail(normalizedEmail, confirmLink, cancellationLink);
            _logger.LogInformation("Confirmation email resent for [{emailHash}]", emailHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to resend confirmation email for [{emailHash}]. Returning masked response.",
                emailHash);
        }

        return GenericSuccess();
    }

    private Result<MessageResponse> GenericSuccess() =>
        Result<MessageResponse>.Success(MessageResponse.Create(GenericResponseMessage));

    private static string HashEmail(string email)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(email));
        return Convert.ToHexString(hashBytes)[..8];
    }

    private async Task SendConfirmationEmail(
        string email,
        string confirmLink,
        string cancellationLink)
    {
        var body = WaitlistConfirmationEmail.BuildBody(confirmLink, cancellationLink);
        await _emailService.SendAsync(email, WaitlistConfirmationEmail.Subject, body);
    }
}
