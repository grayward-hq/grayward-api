using Microsoft.Extensions.Configuration;

namespace Application.Features.Waitlist;

/// <summary>
/// Optional branding used to dress waitlist emails: hosted image assets, the "Back to Home" target
/// and the footer's social links.
/// </summary>
/// <remarks>
/// <para>
/// Every value here is optional and every one is validated to an absolute http(s) URL, falling back
/// to null when absent or malformed. This is deliberately unlike <see cref="WaitlistLinks"/>, which
/// throws on missing config: a confirmation link that points at the wrong host is a security problem,
/// whereas a missing logo is cosmetic. Branding must never be the reason a waitlist email fails to
/// send, so templates degrade — text wordmark instead of a logo, no mascot, no button — rather than
/// blow up.
/// </para>
/// <para>
/// <c>FrontendUrl:AssetsBase</c> is the public base URL that the image files are served from, e.g.
/// <c>https://vulnwatch.com.ng/email</c>; templates append their own filenames to it. Images are
/// referenced by URL rather than embedded because Gmail strips inline SVG and blocks <c>data:</c>
/// URIs outright.
/// </para>
/// </remarks>
internal sealed record WaitlistEmailBranding(
    string? AssetsBase,
    string? HomeUrl,
    string? XUrl,
    string? FacebookUrl,
    string? LinkedInUrl)
{
    public static WaitlistEmailBranding From(IConfiguration config) => new(
        AssetsBase: ReadUrl(config, "FrontendUrl:AssetsBase")?.TrimEnd('/'),
        HomeUrl: ReadUrl(config, "FrontendUrl:Home"),
        XUrl: ReadUrl(config, "Social:X"),
        FacebookUrl: ReadUrl(config, "Social:Facebook"),
        LinkedInUrl: ReadUrl(config, "Social:LinkedIn"));

    /// <summary>True when image assets are available, so templates can use them instead of fallbacks.</summary>
    public bool HasAssets => AssetsBase is not null;

    /// <summary>Absolute URL for an asset filename, or null when no assets base is configured.</summary>
    public string? Asset(string fileName) => AssetsBase is null ? null : $"{AssetsBase}/{fileName}";

    private static string? ReadUrl(IConfiguration config, string key)
    {
        var value = config[key];
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            return null;

        // Note: not a pattern match — UriScheme* are static readonly fields, not constants.
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        return uri.ToString();
    }
}
