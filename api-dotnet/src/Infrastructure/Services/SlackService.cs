using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Features.Integrations.Slack.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Meta;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public sealed class SlackService(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ITokenService tokenProtector,
    ILogger<SlackService> logger) : ISlackService
{
    public async Task<Result<SlackToken>> ExchangeCode(string code, CancellationToken ct)
    {
        var clientId     = config["Slack:ClientId"]!;
        var clientSecret = config["Slack:ClientSecret"]!;
        var redirectUri  = config["Slack:RedirectUri"]!;

        var http = httpClientFactory.CreateClient("slack");

        var response = await http.PostAsync(
            "https://slack.com/api/oauth.v2.access",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"]     = clientId,
                ["client_secret"] = clientSecret,
                ["code"]          = code,
                ["redirect_uri"]  = redirectUri,
            }),
            ct);

        var rawBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Slack token exchange HTTP error: {Status}", response.StatusCode);
            return Result<SlackToken>.Failure(Error.Internal($"HTTP {response.StatusCode}"));
        }

        JsonElement json;
        try
        {
            json = JsonSerializer.Deserialize<JsonElement>(rawBody);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Slack OAuth response was not valid JSON — RawBody: {Body}", rawBody);
            return Result<SlackToken>.Failure(Error.Internal("Slack returned an unparseable response."));
        }

        if (!json.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
        {
            var slackError = json.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
            logger.LogWarning("Slack OAuth returned ok=false: {Error}", slackError);
            return Result<SlackToken>.Failure(Error.Internal($"Slack OAuth failed: {slackError}"));
        }

        var missing = new List<string>();

        string? appId       = GetString(json, "app_id",       missing);
        string? scope       = GetString(json, "scope",        missing);
        string? tokenType   = GetString(json, "token_type",   missing);
        string? accessToken = GetString(json, "access_token", missing);
        string? botUserId   = GetString(json, "bot_user_id",  missing);

        bool isEnterpriseInstall = json.TryGetProperty("is_enterprise_install", out var entProp)
            && entProp.GetBoolean();

        string? teamId   = null;
        string? teamName = null;
        if (json.TryGetProperty("team", out var team))
        {
            teamId   = GetString(team, "id",   missing, prefix: "team.");
            teamName = GetString(team, "name", missing, prefix: "team.");
        }
        else
        {
            missing.Add("team");
        }

        string? authedUserId = null;
        if (json.TryGetProperty("authed_user", out var authedUser))
        {
            authedUserId = GetString(authedUser, "id", missing, prefix: "authed_user.");
        }
        else
        {
            missing.Add("authed_user");
        }

        SlackIncomingWebhook? incomingWebhook = null;
        if (json.TryGetProperty("incoming_webhook", out var webhook))
        {
            var whMissing = new List<string>();
            string? channel          = GetString(webhook, "channel",           whMissing, prefix: "incoming_webhook.");
            string? channelId        = GetString(webhook, "channel_id",        whMissing, prefix: "incoming_webhook.");
            string? configurationUrl = GetString(webhook, "configuration_url", whMissing, prefix: "incoming_webhook.");
            string? url              = GetString(webhook, "url",               whMissing, prefix: "incoming_webhook.");

            if (whMissing.Count > 0)
                missing.AddRange(whMissing);
            else
                incomingWebhook = new SlackIncomingWebhook(channel!, channelId!, configurationUrl!, url!);
        }

        if (missing.Count > 0)
        {
            logger.LogWarning(
                "Slack OAuth response missing required fields: {Fields} — RawBody: {Body}",
                string.Join(", ", missing), rawBody);
            return Result<SlackToken>.Failure(
                Error.Internal($"Slack response missing fields: {string.Join(", ", missing)}"));
        }

        return Result<SlackToken>.Success(
            SlackToken.Success(
                appId!,
                new SlackAuthedUser(authedUserId!),
                scope!,
                tokenType!,
                accessToken!,
                botUserId!,
                new SlackTeam(teamId!, teamName!),
                isEnterpriseInstall,
                incomingWebhook!));
    }

    public async Task SendMessage(
        string botToken, string channelId, string text,
        object? blocks = null, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("slack");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", botToken);

        object payload = blocks is not null
            ? new { channel = channelId, text, blocks }
            : new { channel = channelId, text };

        var content = new StringContent(
            JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync(
            "https://slack.com/api/chat.postMessage", content, ct);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken: ct);

        if (!json.GetProperty("ok").GetBoolean())
            logger.LogWarning("Slack message failed: {Error}",
                json.GetProperty("error").GetString());
    }

    public async Task SendMessageViaWebhookUrl(
    string webhookUrl, string text,
    object? blocks = null, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("slack");

        object payload = blocks is not null
            ? new { text, blocks }
            : new { text };

        var content = new StringContent(
            JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync(webhookUrl, content, ct);
        response.EnsureSuccessStatusCode();
    }

    private static string? GetString(
        JsonElement element,
        string propertyName,
        List<string> missing,
        string prefix = "")
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            var value = prop.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        missing.Add($"{prefix}{propertyName}");
        return null;
    }

    private string ResolveToken(Integration integration)
    {
        var raw = integration.Metadata.TryGetValue(SlackMetadataKeys.BotAccessToken, out var v)
            ? v : throw new InvalidOperationException("Slack bot token not found in integration metadata.");

        return tokenProtector.Unprotect(raw);
    }

}
