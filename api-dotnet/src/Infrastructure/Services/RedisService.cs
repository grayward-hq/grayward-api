using System.Text.Json;
using Application.Features.Chat.DTOs;
using Application.Features.Scans.DTOs;
using Application.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Infrastructure.Services;

public class RedisService : IRedisService
{
    private readonly ILogger<RedisService> _logger;
    private readonly IConnectionMultiplexer _redis;
    private static readonly TimeSpan ChatSessionTtl = TimeSpan.FromHours(2);

    private static readonly TimeSpan SlackStateTtl = TimeSpan.FromMinutes(10);

    public RedisService(ILogger<RedisService> logger, IConnectionMultiplexer redis)
    {
        _logger = logger;
        _redis = redis;
    }

    public async Task PublishScanJob(string queueKey, ScanJob job, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var payload = JsonSerializer.Serialize(job);

        await db.ListLeftPushAsync(queueKey, payload);

        _logger.LogInformation("Scan job published for domain {DomainId}, scan {ScanId}",
            job.DomainId, job.ScanId);
    }

    public async Task<ChatSession?> GetChatSession(Guid sessionId, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var raw = await db.StringGetAsync(Key(sessionId));
        return raw.IsNullOrEmpty ? null : JsonSerializer.Deserialize<ChatSession>(raw!);
    }

    public async Task SetChatSession(ChatSession session, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(session);
        await db.StringSetAsync(Key(session.SessionId), json, ChatSessionTtl);
    }

    public async Task<Guid> CreateChatSession(Guid scanId, CancellationToken ct)
    {
        var sessionId = Guid.NewGuid();
        var session = new ChatSession(sessionId, scanId, []);
        await SetChatSession(session, ct);
        return sessionId;
    }

    public async Task DeleteChatSession(Guid sessionId, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(Key(sessionId));
    }

    private static string Key(Guid sessionId) => $"chat-session:{sessionId}";
    
    public async Task SaveSlackState(string state, Guid userId, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(
            SlackStateKey(state),
            userId.ToString(),
            SlackStateTtl);
    }

    public async Task<Guid?> ValidateSlackState(string state, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var key = SlackStateKey(state);

        // Atomic get-and-delete — prevents replay attacks
        var value = await db.StringGetDeleteAsync(key);

        if (value.IsNullOrEmpty)
            return null;

        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static string SlackStateKey(string state) => $"slack:oauth:state:{state}";

}