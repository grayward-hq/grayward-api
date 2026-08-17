using System.Text.Json;
using Application.Features.Waitlist;
using Application.Interfaces;
using Infrastructure.Redis;
using StackExchange.Redis;

namespace Web.Workers.Waitlist;

/// <summary>
/// Drains the waitlist mail queue, handing each job to <see cref="IWaitlistMailDispatcher"/>.
/// </summary>
/// <remarks>
/// <para>
/// The waitlist endpoints that mail an arbitrary address used to do that work inline. Their response
/// bodies were already masked, but the time they took was not: a miss returned after a single
/// lookup while a hit also claimed a throttle slot, generated a token, wrote to the database and
/// awaited an SMTP round-trip. That difference was measurable, which partially defeated the masking.
/// </para>
/// <para>
/// With the work out here the request path is uniform — handlers enqueue and return, having done
/// nothing whose cost depends on the recipient. Everything that varies happens on this thread, where
/// nobody is timing it.
/// </para>
/// <para>
/// Deliberately thin: the decisions live in the dispatcher, in Application, where the email
/// templates are and where they can be tested without standing up a hosted service.
/// </para>
/// </remarks>
public class WaitlistMailWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WaitlistMailWorker> _logger;
    private readonly string _queue;

    public WaitlistMailWorker(
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<WaitlistMailWorker> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _queue = WaitlistMailQueueName.Resolve(config);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("WaitlistMailWorker listening on {Queue}", _queue);
        var db = _redis.GetDatabase();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await db.ListRightPopAsync(_queue);

                if (result.IsNullOrEmpty)
                {
                    await Task.Delay(500, ct);
                    continue;
                }

                var job = JsonSerializer.Deserialize<WaitlistMailJob>(result.ToString());
                if (job is null)
                {
                    _logger.LogWarning("Discarded an unreadable waitlist mail job");
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IWaitlistMailDispatcher>();
                await dispatcher.DispatchAsync(job, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Never log the address. These jobs exist precisely because which addresses are on
                // the list is the thing being protected; the dispatcher logs a hash instead.
                _logger.LogError(ex, "Error processing a waitlist mail job");
                await Task.Delay(1000, ct);
            }
        }
    }
}
