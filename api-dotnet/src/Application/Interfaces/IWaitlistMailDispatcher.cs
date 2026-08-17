using Application.Features.Waitlist;

namespace Application.Interfaces;

/// <summary>
/// Sends one queued waitlist message. Implementations re-check eligibility and the send throttle at
/// dispatch time; the job itself is only a request, never an assertion that a send should happen.
/// </summary>
public interface IWaitlistMailDispatcher
{
    Task DispatchAsync(WaitlistMailJob job, CancellationToken ct = default);
}
