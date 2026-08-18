using System.Text.Json;
using Application.Features.Waitlist;
using Application.Interfaces;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace Infrastructure.Redis;

/// <summary>
/// Redis list-backed queue for waitlist mail jobs, matching the producer/consumer shape already used
/// for scan jobs and domain intel.
/// </summary>
public class WaitlistMailQueue : IWaitlistMailQueue
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _queue;

    public WaitlistMailQueue(IConnectionMultiplexer redis, IConfiguration config)
    {
        _redis = redis;
        _queue = WaitlistMailQueueName.Resolve(config);
    }

    public async Task EnqueueAsync(WaitlistMailJob job, CancellationToken ct = default)
    {
        // Pushed left, popped right by the worker, so jobs are handled first in first out.
        await _redis.GetDatabase().ListLeftPushAsync(_queue, JsonSerializer.Serialize(job));
    }
}

/// <summary>Single source for the queue name, shared by the producer and the worker.</summary>
public static class WaitlistMailQueueName
{
    public static string Resolve(IConfiguration config)
        => config["Worker:WaitlistMailQueue"] ?? "waitlist-mail";
}
