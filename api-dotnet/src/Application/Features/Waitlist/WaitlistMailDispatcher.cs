using Application.Common.Email;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Application.Features.Waitlist;

/// <summary>
/// Builds and sends one queued waitlist message, re-checking eligibility at send time.
/// </summary>
/// <remarks>
/// <para>
/// This lives in Application rather than in the hosted worker for two reasons: the email templates
/// are internal to this assembly, and keeping the decisions here makes them testable without
/// standing up a <c>BackgroundService</c>. The worker is deliberately a thin loop that pops a job
/// and calls <see cref="DispatchAsync"/>.
/// </para>
/// <para>
/// Eligibility is re-read rather than trusted from the job. Jobs carry no entry state, so a queued
/// message cannot describe an address that was never on the list, and an entry that changed between
/// enqueue and send is judged on what is true now.
/// </para>
/// </remarks>
public class WaitlistMailDispatcher : IWaitlistMailDispatcher
{
    private readonly IWaitlistRepository _waitlistRepo;
    private readonly IEmailService _emailService;
    private readonly IRedisService _redis;
    private readonly IWaitlistCancellationTokenService _cancellationTokens;
    private readonly UserManager<User> _userManager;
    private readonly IConfiguration _config;
    private readonly ILogger<WaitlistMailDispatcher> _logger;

    public WaitlistMailDispatcher(
        IWaitlistRepository waitlistRepo,
        IEmailService emailService,
        IRedisService redis,
        IWaitlistCancellationTokenService cancellationTokens,
        UserManager<User> userManager,
        IConfiguration config,
        ILogger<WaitlistMailDispatcher> logger)
    {
        _waitlistRepo = waitlistRepo;
        _emailService = emailService;
        _redis = redis;
        _cancellationTokens = cancellationTokens;
        _userManager = userManager;
        _config = config;
        _logger = logger;
    }

    public async Task DispatchAsync(WaitlistMailJob job, CancellationToken ct = default)
    {
        var hash = WaitlistEmailLog.Hash(job.Email);

        // One throttle covering every kind, claimed here rather than on the request path. A caller
        // hammering an endpoint now floods the queue instead of the inbox, and the slot decides what
        // actually goes out.
        if (!await _redis.TryClaimEmailCooldownSlot(
                WaitlistEmailThrottle.Purpose, job.Email, WaitlistEmailThrottle.Cooldown(_config), ct))
        {
            _logger.LogInformation("Waitlist mail throttled for [{emailHash}] ({kind})", hash, job.Kind);
            return;
        }

        var branding = VulnwatchEmailBranding.From(_config);

        switch (job.Kind)
        {
            case WaitlistMailKind.AlreadyRegisteredNotice:
                await SendAlreadyRegistered(branding, job, hash);
                break;
            case WaitlistMailKind.AlreadyJoinedNotice:
                await SendAlreadyJoined(branding, job, hash, ct);
                break;
            case WaitlistMailKind.CancellationLink:
                await SendCancellationLink(branding, job, hash, ct);
                break;
            case WaitlistMailKind.ResendConfirmation:
                await SendResendConfirmation(branding, job, hash, ct);
                break;
            default:
                _logger.LogWarning("Unknown waitlist mail kind {kind}", job.Kind);
                break;
        }
    }

    private async Task SendAlreadyRegistered(VulnwatchEmailBranding branding, WaitlistMailJob job, string hash)
    {
        // Only truthful if the address still has an account.
        if (await _userManager.FindByEmailAsync(job.Email) is null)
        {
            _logger.LogInformation("Already-registered notice skipped for [{emailHash}]: no account", hash);
            return;
        }

        await _emailService.SendAsync(
            job.Email,
            WaitlistAlreadyRegisteredEmail.Subject,
            WaitlistAlreadyRegisteredEmail.BuildBody(branding));

        _logger.LogInformation("Already-registered notice sent for [{emailHash}]", hash);
    }

    private async Task SendAlreadyJoined(
        VulnwatchEmailBranding branding, WaitlistMailJob job, string hash, CancellationToken ct)
    {
        var entry = await _waitlistRepo.FindByEmail(job.Email, ct);
        if (entry is null || entry.Status == WaitlistStatus.Cancelled)
        {
            _logger.LogInformation("Already-joined notice skipped for [{emailHash}]: not on the list", hash);
            return;
        }

        // Position is only meaningful for a confirmed entry — the live rank among active entries.
        long? livePosition = null;
        int? totalConfirmed = null;
        if (entry.Status == WaitlistStatus.EmailConfirmed && entry.Position is long sequence)
        {
            livePosition = await _waitlistRepo.GetLivePosition(sequence, ct);
            totalConfirmed = await _waitlistRepo.CountByStatus(WaitlistStatus.EmailConfirmed, ct);
        }

        await _emailService.SendAsync(
            entry.Email,
            WaitlistAlreadyJoinedEmail.Subject,
            WaitlistAlreadyJoinedEmail.BuildBody(branding, entry.Status, livePosition, totalConfirmed));

        _logger.LogInformation("Already-joined notice sent for [{emailHash}]", hash);
    }

    private async Task SendCancellationLink(
        VulnwatchEmailBranding branding, WaitlistMailJob job, string hash, CancellationToken ct)
    {
        var entry = await _waitlistRepo.FindByEmail(job.Email, ct);
        if (entry is null)
        {
            _logger.LogInformation("Cancellation link skipped for [{emailHash}]: not on the list", hash);
            return;
        }

        if (entry.Status is WaitlistStatus.Cancelled or WaitlistStatus.Promoted)
        {
            _logger.LogInformation(
                "Cancellation link skipped for [{emailHash}] with status {status}", hash, entry.Status);
            return;
        }

        var token = _cancellationTokens.GenerateToken(entry.Id, job.Email);
        var link = WaitlistLinks.BuildCancellationLink(
            _config, request: null, job.Email, token, job.Origin);

        await _emailService.SendAsync(
            job.Email,
            WaitlistCancellationEmail.Subject,
            WaitlistCancellationEmail.BuildBody(branding, link));

        _logger.LogInformation("Cancellation link sent for [{emailHash}]", hash);
    }

    private async Task SendResendConfirmation(
        VulnwatchEmailBranding branding, WaitlistMailJob job, string hash, CancellationToken ct)
    {
        var entry = await _waitlistRepo.FindByEmail(job.Email, ct);

        // Nothing to resend: the spot is either already confirmed, cancelled, or promoted.
        if (entry is null
            || entry.EmailConfirmed
            || entry.Status is WaitlistStatus.Cancelled or WaitlistStatus.Promoted)
        {
            _logger.LogInformation("Resend skipped for [{emailHash}]: nothing to confirm", hash);
            return;
        }

        // Reuse the original confirmation token so the resent link is identical to the first email.
        // A pending entry always has one from join; regenerate defensively only if it is missing.
        // The write only happens in that rare case, and it happens here rather than on the request
        // path, so it cannot show up in response time.
        var confirmationToken = entry.EmailConfirmationToken;
        if (string.IsNullOrEmpty(confirmationToken))
        {
            confirmationToken = WaitlistTokens.NewConfirmationToken();
            entry.GenerateEmailConfirmationToken(confirmationToken);
            _waitlistRepo.Update(entry);
            await _waitlistRepo.SaveChangesAsync(ct);
        }

        var confirmLink = WaitlistLinks.BuildConfirmationLink(
            _config, request: null, job.Email, confirmationToken, job.Origin);

        // A distinct cancellation token — the confirmation token must not double as the opt-out
        // credential, or following the cancel link would consume the confirmation.
        var cancellationLink = WaitlistLinks.BuildCancellationLink(
            _config, request: null, job.Email,
            _cancellationTokens.GenerateToken(entry.Id, job.Email), job.Origin);

        await _emailService.SendAsync(
            job.Email,
            WaitlistConfirmationEmail.Subject,
            WaitlistConfirmationEmail.BuildBody(branding, confirmLink, cancellationLink));

        _logger.LogInformation("Confirmation resent for [{emailHash}]", hash);
    }
}
