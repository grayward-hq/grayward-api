using Application.Common.Email;
using Application.Features.Auth.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Application.Features.Waitlist.Commands;

public record RequestWaitlistCancellationCommand(string Email)
    : IRequest<Result<MessageResponse>>;

public class RequestWaitlistCancellationHandler
    : IRequestHandler<RequestWaitlistCancellationCommand, Result<MessageResponse>>
{
    private const string GenericResponseMessage =
        "If this email is on the waitlist, a cancellation link has been sent.";

    private readonly IWaitlistRepository _waitlistRepo;
    private readonly IWaitlistCancellationTokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly IRedisService _redis;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<RequestWaitlistCancellationHandler> _logger;

    public RequestWaitlistCancellationHandler(
        IWaitlistRepository waitlistRepo,
        IWaitlistCancellationTokenService tokenService,
        IEmailService emailService,
        IConfiguration config,
        IRedisService redis,
        IHttpContextAccessor http,
        ILogger<RequestWaitlistCancellationHandler> logger)
    {
        _waitlistRepo = waitlistRepo;
        _tokenService = tokenService;
        _emailService = emailService;
        _config = config;
        _redis = redis;
        _http = http;
        _logger = logger;
    }

    public async Task<Result<MessageResponse>> Handle(
        RequestWaitlistCancellationCommand cmd,
        CancellationToken ct)
    {
        var normalizedEmail = cmd.Email.Trim().ToLowerInvariant();

        var entry = await _waitlistRepo.FindByEmail(normalizedEmail, ct);
        if (entry is null)
        {
            _logger.LogInformation("Cancellation link request masked for non-existent email");

            return GenericSuccess();
        }

        var emailHash = HashEmail(normalizedEmail);

        if (entry.Status == WaitlistStatus.Cancelled || entry.Status == WaitlistStatus.Promoted)
        {
            _logger.LogInformation(
                "Cancellation link request ignored for [{emailHash}] with status {status}.",
                emailHash,
                entry.Status);

            return GenericSuccess();
        }

        // Throttle per recipient: this endpoint mails an arbitrary address on request, so cap it to
        // prevent inbox flooding. Skip silently (masked response unchanged) when a recent send holds
        // the slot.
        if (!await _redis.TryClaimEmailCooldownSlot(
                WaitlistEmailThrottle.Purpose, normalizedEmail, WaitlistEmailThrottle.Cooldown(_config), ct))
        {
            _logger.LogInformation("Cancellation link request throttled for [{emailHash}]", emailHash);
            return GenericSuccess();
        }

        try
        {
            var token = _tokenService.GenerateToken(entry.Id, normalizedEmail);
            var cancellationLink = WaitlistLinks.BuildCancellationLink(
                _config, _http.HttpContext?.Request, normalizedEmail, token);

            await SendCancellationEmail(normalizedEmail, cancellationLink);

            _logger.LogInformation("Cancellation link sent for [{emailHash}]", emailHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send waitlist cancellation link for [{emailHash}]. Returning masked response.",
                emailHash);
        }

        return GenericSuccess();
    }

    private static string HashEmail(string email)
    {
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(email));
            return System.Convert.ToHexString(hashBytes)[..8]; // First 8 chars of hash
        }
    }

    private Result<MessageResponse> GenericSuccess() =>
        Result<MessageResponse>.Success(MessageResponse.Create(GenericResponseMessage));

    private async Task SendCancellationEmail(string email, string cancellationLink)
    {
        var body = WaitlistCancellationEmail.BuildBody(
            VulnwatchEmailBranding.From(_config), cancellationLink);
        await _emailService.SendAsync(email, WaitlistCancellationEmail.Subject, body);
    }
}
