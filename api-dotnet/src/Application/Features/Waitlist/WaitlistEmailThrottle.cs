using Microsoft.Extensions.Configuration;

namespace Application.Features.Waitlist;

/// <summary>
/// Per-recipient cooldown for waitlist emails that an unauthenticated caller can trigger for an
/// arbitrary address (resend, cancellation-link request, and the "already on the list" / "already
/// registered" notices). IP-based rate limiting caps request volume per source, but not how many
/// emails a rotating-IP caller can dump on one victim's inbox — this cooldown does, by allowing at
/// most one such email per address per window. The genuine, user-initiated join confirmation is not
/// throttled here so an expected email is never dropped.
/// </summary>
internal static class WaitlistEmailThrottle
{
    public const string Purpose = "waitlist";

    private const int DefaultCooldownSeconds = 60;

    public static TimeSpan Cooldown(IConfiguration config)
    {
        // Read via the indexer (not GetValue/GetSection) so the value is resolved directly from the
        // key; falls back to the default when unset or invalid.
        var seconds = int.TryParse(config["Waitlist:EmailCooldownSeconds"], out var configured) && configured >= 1
            ? configured
            : DefaultCooldownSeconds;
        return TimeSpan.FromSeconds(seconds);
    }
}
