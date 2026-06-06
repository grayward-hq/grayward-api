using Application.Features.Integrations.Slack.DTOs;
using Domain.Common;

namespace Application.Interfaces;

public interface ISlackService
{
    Task<Result<SlackToken>> ExchangeCode(string code, CancellationToken ct);

    Task SendMessage(
        string botToken, string channelId, string text,
        object? blocks = null, CancellationToken ct = default);

    Task SendMessageViaWebhookUrl(string webhookUrl, string subject, object? blocks = null, CancellationToken ct = default);
}