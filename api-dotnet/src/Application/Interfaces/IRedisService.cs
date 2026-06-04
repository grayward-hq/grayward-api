using Application.Features.Chat.DTOs;
using Application.Features.Scans.DTOs;

namespace Application.Interfaces;

public interface IRedisService
{
    Task PublishScanJob(string queue, ScanJob job, CancellationToken ct = default);
    Task<ChatSession?> GetChatSession(Guid sessionId, CancellationToken ct);
    Task SetChatSession(ChatSession session, CancellationToken ct);
    Task<Guid> CreateChatSession(Guid scanId, CancellationToken ct);
    Task DeleteChatSession(Guid sessionId, CancellationToken ct);
    Task SaveSlackState(string state, Guid userId, CancellationToken ct);
    Task<Guid?> ValidateSlackState(string state, CancellationToken ct);
}
