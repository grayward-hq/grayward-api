using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Application.Features.Waitlist;

/// <summary>
/// Builds the frontend links used in waitlist emails. Shared by the join, resend and verify flows so
/// the same link is produced everywhere — a resent confirmation must be identical to the one mailed
/// on join.
/// </summary>
/// <remarks>
/// <para>
/// Each link requires its own explicit setting (<c>FrontendUrl:WaitlistVerify</c>, etc.) and throws
/// when it is missing. There is deliberately no base-url or localhost fallback: a missing setting is
/// a deployment error, and silently mailing a link to the wrong host is worse than failing loudly.
/// </para>
/// <para>
/// The link's <em>host</em> can additionally track the environment the request came from, so a join
/// submitted from the test frontend gets a test link rather than the configured production one. This
/// is gated by a strict allowlist (<c>FrontendUrl:AllowedOrigins</c>): the request's Origin/Referer
/// is used ONLY if it exactly matches an allowlisted origin; anything else (including a spoofed
/// header from a non-browser client) falls back to the configured URL. Without this guard, reflecting
/// a request header into an emailed link would be host-header injection — an attacker could point a
/// victim's confirmation link, token and all, at a domain they control. The path always comes from
/// the trusted configured URL; only the scheme/host/port is ever swapped.
/// </para>
/// </remarks>
internal static class WaitlistLinks
{
    /// <param name="preferredOrigin">
    /// An origin captured earlier, for callers with no live request — the background mail worker
    /// builds these links off the request thread. Re-validated against the allowlist here, so a
    /// stored origin that has since been removed from config is rejected like any other.
    /// </param>
    public static string BuildConfirmationLink(
        IConfiguration config, HttpRequest? request, string email, string token, string? preferredOrigin = null)
        => $"{ResolveBaseUrl(config, request, "FrontendUrl:WaitlistVerify", preferredOrigin)}" +
           $"?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";

    /// <inheritdoc cref="BuildConfirmationLink"/>
    public static string BuildCancellationLink(
        IConfiguration config, HttpRequest? request, string email, string token, string? preferredOrigin = null)
        => $"{ResolveBaseUrl(config, request, "FrontendUrl:WaitlistCancel", preferredOrigin)}" +
           $"?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";

    public static string BuildReferralLink(
        IConfiguration config, HttpRequest? request, string referralCode, string? preferredOrigin = null)
        => $"{ResolveBaseUrl(config, request, "FrontendUrl:WaitlistJoin", preferredOrigin)}" +
           $"?ref={Uri.EscapeDataString(referralCode)}";

    /// <summary>
    /// Returns the live request's origin (scheme://host[:port]) if it is allowlisted, else null.
    /// Used at join time to capture the environment for later, header-less flows (e.g. verify).
    /// </summary>
    public static string? ResolveAllowedOrigin(IConfiguration config, HttpRequest? request)
        => ResolveAllowedOrigin(config, request, preferredOrigin: null);

    private static string ResolveBaseUrl(
        IConfiguration config, HttpRequest? request, string key, string? preferredOrigin = null)
    {
        var configured = config[key];
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                $"{key} is not configured; cannot build the waitlist link.");

        if (!Uri.TryCreate(configured.TrimEnd('/'), UriKind.Absolute, out var configuredUri))
            throw new InvalidOperationException(
                $"{key} ('{configured}') is not a valid absolute URL.");

        // Keep the trusted path from config; only the origin may be swapped, and only to an
        // allowlisted one — a persisted join origin if given, else where the request came from.
        var origin = ResolveAllowedOrigin(config, request, preferredOrigin);
        if (origin is null)
            return configured.TrimEnd('/');

        var path = configuredUri.AbsolutePath.TrimEnd('/');
        return $"{origin}{path}";
    }

    /// <summary>
    /// Resolves the origin (scheme://host[:port]) to use, preferring a persisted origin over the live
    /// request's Origin/Referer, and returning a value ONLY when it is present in the allowlist. Every
    /// candidate — persisted or live — is re-validated here, so an origin that was allowlisted at join
    /// but later removed from config is rejected. Null means "keep the configured URL".
    /// </summary>
    private static string? ResolveAllowedOrigin(IConfiguration config, HttpRequest? request, string? preferredOrigin)
    {
        var allowed = ReadAllowedOrigins(config);
        if (allowed.Count == 0)
            return null; // Feature off unless origins are configured — preserves legacy behavior.

        foreach (var candidate in new[] { preferredOrigin, GetRawRequestOrigin(request) })
        {
            var origin = NormalizeOrigin(candidate);
            if (origin is not null && allowed.Contains(origin))
                return origin;
        }

        return null;
    }

    private static string? GetRawRequestOrigin(HttpRequest? request)
    {
        if (request is null)
            return null;

        // Prefer the Origin header (sent on the frontend's cross-origin POST); fall back to Referer.
        var origin = request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(origin))
            return origin;

        var referer = request.Headers.Referer.ToString();
        return string.IsNullOrWhiteSpace(referer) ? null : referer;
    }

    private static HashSet<string> ReadAllowedOrigins(IConfiguration config)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        // Support both a bound array (FrontendUrl:AllowedOrigins:0=...) and a single CSV string
        // (FrontendUrl__AllowedOrigins=https://a,https://b) for env-var friendliness.
        var section = config.GetSection("FrontendUrl:AllowedOrigins");
        if (section is null)
            return result; // A real IConfiguration never returns null here; guards test doubles.

        foreach (var child in section.GetChildren())
        {
            var normalized = NormalizeOrigin(child.Value);
            if (normalized is not null)
                result.Add(normalized);
        }

        if (result.Count == 0)
        {
            var csv = section.Value;
            if (!string.IsNullOrWhiteSpace(csv))
            {
                foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var normalized = NormalizeOrigin(part);
                    if (normalized is not null)
                        result.Add(normalized);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Reduces a URL or origin string to a canonical scheme://host[:port] (default ports dropped,
    /// lowercased), or null if it is not a valid absolute http(s) URL.
    /// </summary>
    private static string? NormalizeOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }
}
