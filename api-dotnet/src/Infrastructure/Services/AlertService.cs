using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Meta;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services;

public class AlertService : IAlertService
{
    private readonly IEmailService _emailService;
    private readonly ISlackService _slackService;
    private readonly IIntegrationRepository _integrations;

    public AlertService(
        IEmailService emailService,
        ISlackService slackService,
        IIntegrationRepository integrations)
    {
        _emailService = emailService;
        _slackService = slackService;
        _integrations = integrations;
    }

    public async Task DeliverEmailAsync(
        IServiceScope scope,
        Alert alert,
        CancellationToken ct)
    {
        var to = await ResolveEmailAsync(scope, alert.UserId, ct);

        if (string.IsNullOrWhiteSpace(to))
        {
            alert.MarkFailed("User email not found.");
            return;
        }

        await _emailService.SendAsync(to, alert.Subject, alert.Body);
        alert.MarkSent();
    }

    public async Task DeliverSlackAsync(
        Alert alert,
        CancellationToken ct)
    {
        var integration = await _integrations.GetByUserAndProvider(
            alert.UserId,
            IntegrationProvider.Slack,
            ct);

        if (integration is null)
        {
            alert.MarkFailed("No active Slack integration.");
            return;
        }

        var webhookUrl = integration.GetMetadata(SlackMetadataKeys.WebhookUrl);

        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            alert.MarkFailed("Slack webhook URL not found.");
            return;
        }

        await _slackService.SendMessageViaWebhookUrl(
            webhookUrl,
            alert.Subject,
            BuildSlackBlocks(alert),
            ct);

        alert.MarkSent();
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

            new
            {
                type = "divider"
            },

            new
            {
                type = "section",
                fields = new object[]
                {
                    new
                    {
                        type = "mrkdwn",
                        text = $"*Severity*\n{severity}"
                    },
                    new
                    {
                        type = "mrkdwn",
                        text = $"*Status*\nCompleted"
                    },
                    new
                    {
                        type = "mrkdwn",
                        text = $"*Alert*\n{alert.Subject}"
                    },
                    new
                    {
                        type = "mrkdwn",
                        text = $"*Completed At*\n{completedAt}"
                    }
                }
            },

            new
            {
                type = "section",
                text = new
                {
                    type = "mrkdwn",
                    text = $"*Summary*\n{alert.Body}"
                }
            },

            new
            {
                type = "divider"
            },

            new
            {
                type = "context",
                elements = new object[]
                {
                    new
                    {
                        type = "mrkdwn",
                        text = "VulnWatch Security Monitoring"
                    }
                }
            }
        };
    }
}