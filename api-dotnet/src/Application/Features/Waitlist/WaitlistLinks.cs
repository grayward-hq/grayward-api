using Microsoft.Extensions.Configuration;

namespace Application.Features.Waitlist;

/// <summary>
/// Builds the frontend links used in waitlist emails. Shared by the join, resend and verify flows
/// so the same link is produced everywhere — a resent confirmation must be identical to the one
/// mailed on join.
/// </summary>
/// <remarks>
/// Each link requires its own explicit setting and throws when it is missing. There is deliberately
/// no base-url or localhost fallback: a missing setting is a deployment error, and silently mailing
/// a link to the wrong host is worse than failing loudly.
/// </remarks>
internal static class WaitlistLinks
{
    public static string BuildConfirmationLink(IConfiguration config, string email, string token)
        => $"{RequiredBaseUrl(config, "FrontendUrl:WaitlistVerify")}" +
           $"?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";

    public static string BuildCancellationLink(IConfiguration config, string email, string token)
        => $"{RequiredBaseUrl(config, "FrontendUrl:WaitlistCancel")}" +
           $"?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";

    public static string BuildReferralLink(IConfiguration config, string referralCode)
        => $"{RequiredBaseUrl(config, "FrontendUrl:WaitlistJoin")}" +
           $"?ref={Uri.EscapeDataString(referralCode)}";

    private static string RequiredBaseUrl(IConfiguration config, string key)
    {
        var baseUrl = config[key];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException(
                $"{key} is not configured; cannot build the waitlist link.");

        return baseUrl.TrimEnd('/');
    }
}
