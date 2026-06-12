using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Meta;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class AlertService : IAlertService
{
    private readonly IEmailService _emailService;
    private readonly ISlackService _slackService;
    private readonly IIntegrationRepository _integrations;
    private readonly ITokenService _tokenProtector;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        IEmailService emailService,
        ISlackService slackService,
        IIntegrationRepository integrations,
        ITokenService tokenProtector,
        ILogger<AlertService> logger)
    {
        _emailService = emailService;
        _slackService = slackService;
        _integrations = integrations;
        _tokenProtector = tokenProtector;
        _logger = logger;
    }

    public async Task DeliverEmailAsync(
        IServiceScope scope,
        Alert alert,
        CancellationToken ct)
    {
        var to = await ResolveEmailAsync(scope, alert.UserId, ct);

        if (string.IsNullOrWhiteSpace(to))
        {
            _logger.LogWarning(
                "[Slack/Email Alert] Cannot deliver alert {AlertId} — no email found for user {UserId}",
                alert.Id, alert.UserId);
            alert.MarkFailed("User email not found.");
            return;
        }

        await _emailService.SendAsync(to, alert.Subject, alert.Body);
        alert.MarkSent();

        _logger.LogInformation(
            "[Email Alert] Delivered alert {AlertId} (type: {AlertType}) to {Email}",
            alert.Id, alert.Type, to);
    }

    public async Task DeliverSlackAsync(
        Alert alert,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "[Slack Alert] Attempting delivery — AlertId: {AlertId}, UserId: {UserId}, Type: {AlertType}, Domain: {DomainId}",
            alert.Id, alert.UserId, alert.Type, alert.DomainId);

        var integration = await _integrations.GetByUserAndProvider(
            alert.UserId,
            IntegrationProvider.Slack,
            ct);

        if (integration is null)
        {
            _logger.LogWarning(
                "[Slack Alert] No active Slack integration found for user {UserId} — alert {AlertId} not sent",
                alert.UserId, alert.Id);
            alert.MarkFailed("No active Slack integration.");
            return;
        }

        _logger.LogInformation(
            "[Slack Alert] Found integration for user {UserId} — Team: {TeamName}, Status: {Status}",
            alert.UserId, integration.GetMetadata(SlackMetadataKeys.TeamName), integration.Status);

        var encryptedToken = integration.GetMetadata(SlackMetadataKeys.BotAccessToken);
        var channelId = integration.GetMetadata(SlackMetadataKeys.WebhookChannelId);
        var channelName = integration.GetMetadata(SlackMetadataKeys.WebhookChannel);

        if (string.IsNullOrWhiteSpace(encryptedToken))
        {
            _logger.LogWarning(
                "[Slack Alert] No bot access token stored for user {UserId} — alert {AlertId} not sent. " +
                "User may need to reconnect Slack.",
                alert.UserId, alert.Id);
            alert.MarkFailed("Slack bot token not found.");
            return;
        }

        if (string.IsNullOrWhiteSpace(channelId))
        {
            _logger.LogWarning(
                "[Slack Alert] No channel ID stored for user {UserId} (channel name on record: '{ChannelName}') — " +
                "alert {AlertId} not sent. User may need to reconnect Slack to capture channel ID.",
                alert.UserId, channelName, alert.Id);
            alert.MarkFailed("Slack channel ID not found.");
            return;
        }

        string botToken;
        try
        {
            botToken = _tokenProtector.Unprotect(encryptedToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Slack Alert] Failed to decrypt bot token for user {UserId} — alert {AlertId} not sent",
                alert.UserId, alert.Id);
            alert.MarkFailed("Failed to decrypt Slack bot token.");
            return;
        }

        try
        {
            _logger.LogInformation(
                "[Slack Alert] Sending alert {AlertId} to channel {ChannelId} ('{ChannelName}') for user {UserId}",
                alert.Id, channelId, channelName, alert.UserId);

            await _slackService.SendMessage(
                botToken, channelId, alert.Subject, BuildSlackBlocks(alert), ct);

            alert.MarkSent();

            _logger.LogInformation(
                "[Slack Alert] Successfully delivered alert {AlertId} to channel {ChannelId}",
                alert.Id, channelId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Slack Alert] Failed to send alert {AlertId} to channel {ChannelId} for user {UserId}: {ErrorMessage}",
                alert.Id, channelId, alert.UserId, ex.Message);
            alert.MarkFailed($"Slack delivery failed: {ex.Message}");
        }
    }

    public async Task<string> ResolveEmailAsync(
        IServiceScope scope,
        Guid userId,
        CancellationToken ct)
    {
        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<User>>();

        var user = await userManager.FindByIdAsync(userId.ToString());

        return user?.Email
            ?? throw new InvalidOperationException(
                $"No email for user {userId}");
    }

    public object BuildSlackBlocks(Alert alert)
    {
        var completedAt =
            alert.CreatedAt.ToString("MMM d, yyyy 'at' HH:mm 'UTC'");

        var severity = alert.Severity.ToString();
        var summary = !string.IsNullOrWhiteSpace(alert.Summary)
        ? alert.Summary
        : "Open the dashboard for full details.";

        return new object[]
        {
            new
            {
                type = "section",
                text = new
                {
                    type = "mrkdwn",
                    text = $"*{alert.Subject}*"
                }
            },
            new { type = "divider" },
            new
            {
                type = "section",
                fields = new object[]
                {
                    new { type = "mrkdwn", text = $"*Severity*\n{severity}" },
                    // new { type = "mrkdwn", text = $"*Status*\nCompleted" },
                    new { type = "mrkdwn", text = $"*Alert*\n{alert.Subject}" },
                    new { type = "mrkdwn", text = $"*Completed At*\n{completedAt}" }
                }
            },
            new
            {
                type = "section",
                text = new { type = "mrkdwn", text = $"*Summary*\n{summary}" }
            },
            new { type = "divider" },
            new
            {
                type = "context",
                elements = new object[]
                {
                    new { type = "mrkdwn", text = "VulnWatch Security Monitoring" }
                }
            }
        };
    }
}