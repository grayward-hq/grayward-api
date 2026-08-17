using Application.Common.Email;
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

    private readonly IWaitlistMailQueue _mailQueue;
    private readonly IConfiguration _config;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<ResendWaitlistConfirmationHandler> _logger;

    public ResendWaitlistConfirmationHandler(
        IWaitlistMailQueue mailQueue,
        IConfiguration config,
        IHttpContextAccessor http,
        ILogger<ResendWaitlistConfirmationHandler> logger)
    {
        _mailQueue = mailQueue;
        _config = config;
        _http = http;
        _logger = logger;
    }

    public async Task<Result<MessageResponse>> Handle(
        ResendWaitlistConfirmationCommand cmd,
        CancellationToken ct)
    {
        var normalizedEmail = cmd.Email.Trim().ToLowerInvariant();

        // Enqueue and return. No lookup, no eligibility check, no token write and no send here:
        // those are what made a hit slower than a miss, and the response is masked either way, so
        // the timing difference was the only signal an attacker could read. The worker decides
        // whether anything is actually sent. See WaitlistMailDispatcher.
        try
        {
            await _mailQueue.EnqueueAsync(
                new WaitlistMailJob(
                    WaitlistMailKind.ResendConfirmation,
                    normalizedEmail,
                    WaitlistLinks.ResolveAllowedOrigin(_config, _http.HttpContext?.Request)),
                ct);
        }
        catch (Exception ex)
        {
            // Swallowed on purpose: a queue outage must not become an oracle of its own by making
            // this endpoint behave differently from its masked contract.
            _logger.LogError(ex, "Failed to enqueue a waitlist confirmation resend");
        }

        return GenericSuccess();
    }

    private Result<MessageResponse> GenericSuccess() =>
        Result<MessageResponse>.Success(MessageResponse.Create(GenericResponseMessage));

}
