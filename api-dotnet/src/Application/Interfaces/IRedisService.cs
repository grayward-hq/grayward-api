using Application.Features.Chat.DTOs;
using Application.Features.Scans.DTOs;
using Domain.Entities;

namespace Application.Interfaces;

public interface IRedisService
{
    // Task PublishScanJob(string queue, Scan job, CancellationToken ct = default);
    Task PublishScanJob(string queue, ScanJob job, CancellationToken ct = default);
    Task<ChatSession?> GetChatSession(Guid sessionId, CancellationToken ct);
    Task SetChatSession(ChatSession session, CancellationToken ct);
    Task<Guid> CreateChatSession(Guid scanId, CancellationToken ct);
    Task DeleteChatSession(Guid sessionId, CancellationToken ct);
    Task SaveSlackState(string state, Guid userId, CancellationToken ct);
    Task<Guid?> ValidateSlackState(string state, CancellationToken ct);

    /// <summary>
    /// Atomically claims a short-lived per-recipient send slot, used to throttle transactional email
    /// so a caller cannot flood an address by hammering an endpoint. Returns true if the slot was
    /// claimed (the caller may send); false if a send for this <paramref name="purpose"/> and
    /// <paramref name="email"/> already happened within <paramref name="cooldown"/> (skip sending).
    /// The email is hashed before use so raw addresses are never written to Redis.
    /// </summary>
    Task<bool> TryClaimEmailCooldownSlot(string purpose, string email, TimeSpan cooldown, CancellationToken ct);
}
