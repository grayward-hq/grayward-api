namespace Application.Features.Waitlist;

/// <summary>Which waitlist message a queued job should produce.</summary>
public enum WaitlistMailKind
{
    /// <summary>Someone asked for a fresh confirmation link (resend flow).</summary>
    ResendConfirmation,

    /// <summary>Someone asked for a link to leave the waitlist.</summary>
    CancellationLink,

    /// <summary>A join attempt hit an address already on the waitlist.</summary>
    AlreadyJoinedNotice,

    /// <summary>A join attempt hit an address that already has a full account.</summary>
    AlreadyRegisteredNotice,
}

/// <summary>
/// A request to send one waitlist message, handed to <c>WaitlistMailWorker</c> through the queue.
/// </summary>
/// <remarks>
/// <para>
/// These messages are triggered by anonymous callers against an arbitrary address, so the endpoints
/// deliberately return an identical masked body whether or not the address is on the waitlist. The
/// body was already uniform; the <em>timing</em> was not, which partially re-enabled enumeration —
/// a miss returned after one lookup, while a hit also did a throttle claim, token generation, a
/// database write and an awaited SMTP round-trip.
/// </para>
/// <para>
/// Enqueuing is the whole of the request path now: no lookup, no eligibility check, no send. Every
/// caller pays the same cost regardless of whether the address exists, and the worker decides
/// afterwards whether anything is actually sent.
/// </para>
/// <para>
/// <paramref name="Origin"/> is captured at enqueue because it is the one thing only the request
/// knows. It is already allowlist-validated by <c>WaitlistLinks.ResolveAllowedOrigin</c>, so a
/// spoofed header cannot steer an emailed link at an attacker's host; null means "use the configured
/// URL". Nothing else request-derived is needed, and no entry data is carried — the worker re-reads
/// it, so a job cannot leak state about an address that was never on the list.
/// </para>
/// </remarks>
/// <param name="Kind">Which message to build.</param>
/// <param name="Email">Normalised recipient address.</param>
/// <param name="Origin">Allowlisted frontend origin from the triggering request, or null.</param>
public record WaitlistMailJob(WaitlistMailKind Kind, string Email, string? Origin);
