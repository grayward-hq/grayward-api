using Domain.Enums;

namespace Application.Features.Chat.DTOs;

public record ChatMessageRequest(string Message)
{
    public static ChatMessageRequest Create(string message) => new(message);
}

public record ChatMessageResponse(
    Guid SessionId,
    ChatMessageRole Role,
    string Content,
    DateTime Timestamp)
{
    public static ChatMessageResponse Create(Guid sessionId, ChatMessageRole role, string content)
        => new(sessionId, role, content, DateTime.UtcNow);
}

public record ChatSession(
    Guid SessionId,
    Guid ScanId,
    IReadOnlyList<ChatTurn> History);

public record ChatTurn(ChatMessageRole Role, string Content);