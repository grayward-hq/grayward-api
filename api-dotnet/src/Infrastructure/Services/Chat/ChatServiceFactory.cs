using Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services.Chat;

public sealed class ChatServiceFactory : IChatServiceFactory
{
    private readonly IServiceProvider _sp;
    private readonly string _provider;

    public ChatServiceFactory(IServiceProvider sp, IConfiguration config)
    {
        _sp       = sp;
        _provider = config["Chat:Provider"]?.ToLowerInvariant() ?? "anthropic";
    }

    public IChatService Resolve() => _provider switch
    {
        "gemini"    => _sp.GetRequiredService<GeminiChatService>(),
        "openai"    => _sp.GetRequiredService<OpenAiChatService>(),
        "groq"      => _sp.GetRequiredService<OpenAiChatService>(),  // same wire format
        "anthropic" => _sp.GetRequiredService<AnthropicChatService>(),
        _           => throw new InvalidOperationException(
                           $"Unknown chat provider '{_provider}'. " +
                           "Valid values: anthropic, gemini, openai, groq")
    };
}