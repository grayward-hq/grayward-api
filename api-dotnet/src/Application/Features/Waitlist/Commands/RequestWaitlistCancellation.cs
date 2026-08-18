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

    private readonly IWaitlistMailQueue _mailQueue;
    private readonly IConfiguration _config;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<RequestWaitlistCancellationHandler> _logger;

    public RequestWaitlistCancellationHandler(
        IWaitlistMailQueue mailQueue,
        IConfiguration config,
        IHttpContextAccessor http,
        ILogger<RequestWaitlistCancellationHandler> logger)
    {
        _mailQueue = mailQueue;
        _config = config;
        _http = http;
        _logger = logger;
    }

    public async Task<Result<MessageResponse>> Handle(
        RequestWaitlistCancellationCommand cmd,
        CancellationToken ct)
    {
        var normalizedEmail = cmd.Email.Trim().ToLowerInvariant();

        // Enqueue and return. Deliberately no lookup, no eligibility check and no send here: those
        // are what made a hit slower than a miss, and the response is masked either way, so the
        // difference was the only thing an attacker could read. The worker decides whether anything
        // is actually sent. See WaitlistMailDispatcher.
        //
        // The origin is resolved here because only the request knows it, and it is allowlist-gated,
        // so a spoofed header cannot steer the emailed link. It costs a header read either way.
        try
        {
            await _mailQueue.EnqueueAsync(
                new WaitlistMailJob(
                    WaitlistMailKind.CancellationLink,
                    normalizedEmail,
                    WaitlistLinks.ResolveAllowedOrigin(_config, _http.HttpContext?.Request)),
                ct);
        }
        catch (Exception ex)
        {
            // Swallowed on purpose: a queue outage must not become an oracle of its own by making
            // this endpoint behave differently from its masked contract.
            _logger.LogError(ex, "Failed to enqueue a waitlist cancellation link request");
        }

        return GenericSuccess();
    }

    private Result<MessageResponse> GenericSuccess() =>
        Result<MessageResponse>.Success(MessageResponse.Create(GenericResponseMessage));

}
