using Application.Features.Chat.DTOs;
using Domain.Enums;

namespace Application.Interfaces;

public interface IChatServiceFactory
{
    IChatService Resolve();
}
public interface IChatService
{
    Task<string> Chat(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        CancellationToken ct);

    IAsyncEnumerable<string> Stream(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        CancellationToken ct);
}