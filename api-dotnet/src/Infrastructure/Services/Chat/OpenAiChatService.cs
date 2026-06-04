using Application.Features.Chat.DTOs;
using Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace Infrastructure.Services.Chat;

public sealed class OpenAiChatService : ChatServiceBase
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly ILogger<OpenAiChatService> _logger;

    protected override ILogger Logger => _logger;
    protected override HttpClient Http => _http;

    public OpenAiChatService(
        IHttpClientFactory factory,
        IConfiguration config,
        ILogger<OpenAiChatService> logger)
    {
        // Named client carries the base URL + auth header — see registration below
        _http = factory.CreateClient("openai");
        _model = config["Chat:OpenAi:Model"] ?? "gpt-4o-mini";
        _logger = logger;
    }

    protected override HttpRequestMessage BuildRequest(
    string systemPrompt,
    IReadOnlyList<ChatTurn> history,
    bool stream)
    {
        var messages = new List<object>
    {
        new { role = "system", content = systemPrompt }
    };

        messages.AddRange(history.Select(t => (object)new
        {
            role = t.Role == ChatMessageRole.Assistant ? "assistant" : "user",
            content = t.Content
        }));

        var payload = new
        {
            model = _model,
            max_tokens = 1024,
            stream,
            messages
        };

        // Use absolute URI if BaseAddress is set, relative otherwise
        // This avoids the HttpClient BaseAddress trailing-slash trap
        return new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = JsonContent.Create(payload)
        };
    }

    protected override string? ParseSseChunk(JsonElement root)
    {
        // OpenAI / Groq SSE shape:
        // { "choices": [{ "delta": { "content": "Hello" } }] }
        if (root.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("delta", out var delta) &&
            delta.TryGetProperty("content", out var content))
        {
            var text = content.GetString();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        return null;
    }
}