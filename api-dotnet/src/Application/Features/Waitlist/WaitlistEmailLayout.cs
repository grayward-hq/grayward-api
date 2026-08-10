using System.Net;

namespace Application.Features.Waitlist;

/// <summary>
/// The shared chrome for branded waitlist emails — logo, mascot, heading, optional position card,
/// "Back to Home" button and footer. Each email supplies only its own copy so the two "already…"
/// notices cannot drift apart visually.
/// </summary>
/// <remarks>
/// <para>
/// Laid out with nested tables and inline styles rather than modern CSS: Outlook renders through
/// Word's HTML engine, which ignores flex/grid and most block-level padding, and several clients
/// strip &lt;style&gt; blocks entirely.
/// </para>
/// <para>
/// Every image comes from <see cref="WaitlistEmailBranding"/> and is optional, so the mail still
/// reads correctly with all images blocked — which is the default in most inboxes until the sender
/// is trusted.
/// </para>
/// </remarks>
internal static class WaitlistEmailLayout
{
    /// <summary>
    /// Text green, deepened from the brand's <c>#A0E870</c>. That fill only reaches ~1.6:1 against the
    /// card and is unreadable for low-vision recipients and on a phone outdoors; this hits ~4.6:1
    /// (WCAG AA) while still reading as the same green. Use <see cref="BrandGreenFill"/> for large
    /// areas of colour, where contrast is not at stake.
    /// </summary>
    public const string BrandGreen = "#4E9A2A";

    public const string BrandGreenFill = "#A0E870";
    public const string BrandDark = "#072E28";
    public const string CardBackground = "#F1F1F1";
    public const string BodyText = "#6B7280";
    public const string MutedText = "#9CA3AF";
    public const string HeadingText = "#111827";
    public const string FontStack = "'Helvetica Neue', Helvetica, Arial, sans-serif";

    /// <param name="title">Document title.</param>
    /// <param name="preheader">Inbox-list preview text; hidden in the rendered body.</param>
    /// <param name="headingLead">Heading text before the green accent.</param>
    /// <param name="headingAccent">Trailing heading words, rendered in the accent green.</param>
    /// <param name="bodyHtml">Body paragraphs, built with <see cref="Paragraph"/>.</param>
    /// <param name="footnote">Small print closing the message.</param>
    /// <param name="positionCard">Optional queue-position card from <see cref="PositionCard"/>.</param>
    public static string Render(
        WaitlistEmailBranding branding,
        string title,
        string preheader,
        string headingLead,
        string headingAccent,
        string bodyHtml,
        string footnote,
        string? positionCard = null)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <title>{title}</title>
</head>
<body style='margin: 0; padding: 0; background-color: #FFFFFF;'>
    <div style='display: none; max-height: 0; overflow: hidden; opacity: 0;'>{preheader}</div>

    <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'
           style='background-color: #FFFFFF;'>
        <tr>
            <td align='center' style='padding: 24px 12px;'>

                <table role='presentation' width='600' cellpadding='0' cellspacing='0' border='0'
                       style='width: 100%; max-width: 600px; background-color: {CardBackground}; border-radius: 16px;'>
                    <tr>
                        <td style='padding: 40px 32px;'>

                            {BuildLogo(branding)}
                            {BuildMascot(branding)}

                            <p style='margin: 0 0 24px 0; font-family: {FontStack}; font-size: 24px;
                                      font-weight: 700; color: {HeadingText}; text-align: center; line-height: 1.3;'>
                                {headingLead} <span style='color: {BrandGreen};'>{headingAccent}</span>
                            </p>

                            {positionCard}
                            {bodyHtml}
                            {BuildHomeButton(branding)}

                            <p style='margin: 0 0 24px 0; font-family: {FontStack}; font-size: 13px;
                                      color: {MutedText}; text-align: center; line-height: 1.6;'>
                                {footnote}
                            </p>

                            <p style='margin: 0 0 20px 0; font-family: {FontStack}; font-size: 13px;
                                      color: {BrandDark}; text-align: center; line-height: 1.6;'>
                                Vulnwatch scans your surface. So you can focus on your business<br>
                                &copy;{DateTime.UtcNow.Year} Vulnwatch
                            </p>

                            {BuildSocialLinks(branding)}

                            <p style='margin: 0 0 8px 0; font-family: {FontStack}; font-size: 13px;
                                      font-weight: 700; color: {HeadingText};'>
                                Need Support?
                            </p>
                            <p style='margin: 0; font-family: {FontStack}; font-size: 12px;
                                      color: {BodyText}; line-height: 1.6;'>
                                Feel free to email us if you have any questions, comments or suggestions.
                                We&rsquo;ll be happy to resolve your issues.
                            </p>

                        </td>
                    </tr>
                </table>

            </td>
        </tr>
    </table>
</body>
</html>";
    }

    /// <summary>A centred body paragraph in the shared type style.</summary>
    public static string Paragraph(string html, bool last = false) => $@"
                            <p style='margin: 0 0 {(last ? 32 : 12)}px 0; font-family: {FontStack};
                                      font-size: 15px; color: {BodyText}; text-align: center; line-height: 1.6;'>
                                {html}
                            </p>";

    /// <summary>
    /// The white "#5 / Out of 126 on the waitlist" card. <paramref name="total"/> is floored at
    /// <paramref name="position"/>: the two numbers are read separately, so a join landing between
    /// them could otherwise produce a nonsensical "#5 out of 4".
    /// </summary>
    public static string PositionCard(long position, int total)
    {
        var denominator = Math.Max(total, position);

        return $@"
                            <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'>
                                <tr>
                                    <td align='center' style='padding-bottom: 24px;'>
                                        <table role='presentation' cellpadding='0' cellspacing='0' border='0'
                                               style='background-color: #FFFFFF; border-radius: 12px;'>
                                            <tr>
                                                <td align='center' style='padding: 24px 48px;'>
                                                    <p style='margin: 0 0 4px 0; font-family: {FontStack}; font-size: 34px;
                                                              font-weight: 700; color: {BrandDark}; line-height: 1.1;'>
                                                        #{position}
                                                    </p>
                                                    <p style='margin: 0; font-family: {FontStack}; font-size: 12px;
                                                              color: {BodyText};'>
                                                        Out of {denominator} on the waitlist
                                                    </p>
                                                </td>
                                            </tr>
                                        </table>
                                    </td>
                                </tr>
                            </table>";
    }

    /// <summary>Logo lockup, falling back to a styled wordmark when no assets are hosted.</summary>
    private static string BuildLogo(WaitlistEmailBranding branding)
    {
        var logo = branding.Asset("vulnwatch-logo.png");

        var mark = logo is null
            ? $@"<span style='font-family: {FontStack}; font-size: 20px; font-weight: 700;
                              letter-spacing: 2px; color: {BrandDark};'>VULNWATCH</span>"
            : $@"<img src='{Encode(logo)}' alt='Vulnwatch' width='180'
                      style='display: block; border: 0; width: 180px; height: auto;'>";

        return $@"
                            <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'>
                                <tr>
                                    <td align='center' style='padding-bottom: 24px;'>{mark}</td>
                                </tr>
                            </table>";
    }

    /// <summary>Decorative mascot; omitted entirely when no assets are hosted.</summary>
    private static string BuildMascot(WaitlistEmailBranding branding)
    {
        var mascot = branding.Asset("vulnwatch-mascot.png");
        if (mascot is null)
            return string.Empty;

        // Decorative only, so alt is deliberately empty — screen readers skip it rather than
        // announcing filler ahead of the actual message.
        return $@"
                            <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'>
                                <tr>
                                    <td align='center' style='padding-bottom: 24px;'>
                                        <img src='{Encode(mascot)}' alt='' width='160'
                                             style='display: block; border: 0; width: 160px; height: auto;'>
                                    </td>
                                </tr>
                            </table>";
    }

    /// <summary>"Back to Home" call to action; omitted when no home URL is configured.</summary>
    private static string BuildHomeButton(WaitlistEmailBranding branding)
    {
        if (branding.HomeUrl is null)
            return string.Empty;

        return $@"
                            <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'>
                                <tr>
                                    <td align='center' style='padding-bottom: 32px;'>
                                        <a href='{Encode(branding.HomeUrl)}'
                                           style='display: inline-block; background-color: {BrandDark}; color: #FFFFFF;
                                                  font-family: {FontStack}; font-size: 15px; font-weight: 700;
                                                  text-decoration: none; padding: 14px 32px; border-radius: 8px;'>
                                            Back to Home
                                        </a>
                                    </td>
                                </tr>
                            </table>";
    }

    /// <summary>
    /// Footer social row. Only configured networks are rendered, so an unset one leaves no dead icon
    /// behind; icons need hosted assets, so without them the links degrade to text.
    /// </summary>
    private static string BuildSocialLinks(WaitlistEmailBranding branding)
    {
        var networks = new[]
        {
            (Url: branding.XUrl, Name: "X", Icon: "social-x.png"),
            (Url: branding.FacebookUrl, Name: "Facebook", Icon: "social-facebook.png"),
            (Url: branding.LinkedInUrl, Name: "LinkedIn", Icon: "social-linkedin.png"),
        };

        var cells = string.Empty;
        foreach (var (url, name, icon) in networks)
        {
            if (url is null)
                continue;

            var content = branding.Asset(icon) is string iconUrl
                ? $@"<img src='{Encode(iconUrl)}' alt='{name}' width='24' height='24'
                          style='display: block; border: 0; width: 24px; height: 24px;'>"
                : $@"<span style='font-family: {FontStack}; font-size: 13px; color: {BrandDark};'>{name}</span>";

            cells += $@"
                                        <td style='padding: 0 8px;'>
                                            <a href='{Encode(url)}' style='text-decoration: none;'>{content}</a>
                                        </td>";
        }

        if (cells.Length == 0)
            return string.Empty;

        return $@"
                            <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'>
                                <tr>
                                    <td align='center' style='padding-bottom: 32px;'>
                                        <table role='presentation' cellpadding='0' cellspacing='0' border='0'>
                                            <tr>{cells}
                                            </tr>
                                        </table>
                                    </td>
                                </tr>
                            </table>";
    }

    /// <summary>
    /// Encodes a config-sourced URL for use in an HTML attribute. These values are operator-controlled
    /// rather than user input, but they land in an attribute delimited by single quotes, so encoding
    /// keeps a stray quote from breaking out of it.
    /// </summary>
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
