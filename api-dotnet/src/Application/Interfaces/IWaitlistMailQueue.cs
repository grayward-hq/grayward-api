using Application.Features.Waitlist;

namespace Application.Interfaces;

/// <summary>
/// Hands a waitlist message off to the background mail worker.
/// </summary>
/// <remarks>
/// Enqueuing must stay cheap and constant-time with respect to whether the address exists — that is
/// the entire point of the queue. Implementations must not look the address up, check eligibility,
/// or do anything else whose cost depends on the recipient.
/// </remarks>
public interface IWaitlistMailQueue
{
    /// <summary>
    /// Queues a job. Failures are the caller's to swallow: the endpoints return a masked response
    /// regardless, and a queue outage must not turn into an enumeration oracle of its own.
    /// </summary>
    Task EnqueueAsync(WaitlistMailJob job, CancellationToken ct = default);
}
