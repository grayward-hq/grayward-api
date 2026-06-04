using Application.Features.Chat.DTOs;
using Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace Infrastructure.Services.Chat;

public sealed class GeminiChatService : ChatServiceBase
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<GeminiChatService> _logger;

    protected override ILogger Logger => _logger;
    protected override HttpClient Http => _http;

    // Gemini stream just ends — no sentinel line
    protected override string DoneSentinel => "__NEVER_MATCHES__";

    public GeminiChatService(
        IHttpClientFactory factory,
        IConfiguration config,
        ILogger<GeminiChatService> logger)
    {
        _http = factory.CreateClient("gemini");
        _apiKey = config["Chat:Gemini:ApiKey"]
                  ?? throw new InvalidOperationException("Chat:Gemini:ApiKey not configured.");
        _model = config["Chat:Gemini:Model"] ?? "gemini-2.0-flash";
        _logger = logger;
    }

    protected override HttpRequestMessage BuildRequest(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        bool stream)
    {
        // Gemini separates the system prompt from the conversation turns
        // and uses a different payload shape
        var contents = history
            .Select(t => new
            {
                role = GeminiRole(t.Role),
                parts = new[] { new { text = t.Content } }
            })
            .ToList();

        var payload = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents,
            generationConfig = new { maxOutputTokens = 1024 }
        };

        var endpoint = stream
            ? $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:streamGenerateContent?alt=sse"
            : $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent";

        var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.Add("x-goog-api-key", _apiKey);
        return req;
    }

    protected override string? ParseSseChunk(JsonElement root)
    {
        // Gemini SSE shape:
        // { "candidates": [{ "content": { "parts": [{ "text": "Hello" }] } }] }
        if (root.TryGetProperty("candidates", out var candidates) &&
            candidates.GetArrayLength() > 0)
        {
            var first = candidates[0];
            if (first.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var text))
            {
                return text.GetString();
            }
        }
        return null;
    }

    private static string GeminiRole(ChatMessageRole role) => role switch
    {
        ChatMessageRole.User => "user",
        ChatMessageRole.Assistant => "model",   // Gemini uses "model" not "assistant"
        _ => "user"
    };
}