using Application.Features.Chat.DTOs;
using Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infrastructure.Services.Chat;

public sealed class AnthropicChatService : ChatServiceBase
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<AnthropicChatService> _logger;

    protected override ILogger Logger => _logger;
    protected override HttpClient Http => _http;

    public AnthropicChatService(
        IHttpClientFactory factory,
        IConfiguration config,
        ILogger<AnthropicChatService> logger)
    {
        _http    = factory.CreateClient("anthropic");
        _apiKey  = config["Chat:Anthropic:ApiKey"]
                   ?? throw new InvalidOperationException("Chat:Anthropic:ApiKey not configured.");
        _model   = config["Chat:Anthropic:Model"] ?? "claude-sonnet-4-20250514";
        _logger  = logger;
    }

    protected override HttpRequestMessage BuildRequest(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        bool stream)
    {
        var messages = history
            .Select(t => new { role = RoleLabel(t.Role), content = t.Content })
            .ToList();

        var payload = new
        {
            model      = _model,
            max_tokens = 1024,
            stream,
            system     = systemPrompt,
            messages
        };

        var req = new HttpRequestMessage(HttpMethod.Post,
            "https://api.anthropic.com/v1/messages")
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        return req;
    }

    protected override string? ParseSseChunk(JsonElement root)
    {
        if (root.TryGetProperty("type", out var type) &&
            type.GetString() == "content_block_delta" &&
            root.TryGetProperty("delta", out var delta) &&
            delta.TryGetProperty("type", out var deltaType) &&
            deltaType.GetString() == "text_delta" &&
            delta.TryGetProperty("text", out var text))
        {
            return text.GetString();
        }
        return null;
    }

    private static string RoleLabel(ChatMessageRole role) => role switch
    {
        ChatMessageRole.User      => "user",
        ChatMessageRole.Assistant => "assistant",
        _                         => "user"
    };
}