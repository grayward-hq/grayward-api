using Application.Common.Email;
namespace Application.Features.Waitlist;

/// <summary>
/// Post-confirmation message: the recipient has verified their address and claimed a queue position.
/// Sent by <see cref="Commands.VerifyWaitlistEmailHandler"/>.
/// </summary>
/// <remarks>
/// Extracted from the handler so every waitlist mail is built the same way, through
/// <see cref="VulnwatchEmailLayout"/>.
/// </remarks>
internal static class WaitlistConfirmedEmail
{
    public const string Subject = "You're on the Vulnwatch waitlist!";

    /// <param name="position">The live queue position just claimed.</param>
    /// <param name="totalConfirmed">Size of the confirmed queue — the card's denominator.</param>
    /// <param name="referralLink">The recipient's personal referral link.</param>
    public static string BuildBody(
        VulnwatchEmailBranding branding,
        long position,
        int totalConfirmed,
        string referralLink) => VulnwatchEmailLayout.Render(
        branding,
        title: "You're in. Spot secured",
        preheader: $"Your spot is reserved — you're #{position} on the Vulnwatch waitlist.",
        headingLead: "You&rsquo;re in. Spot",
        headingAccent: "secured",
        positionCard: VulnwatchEmailLayout.PositionCard(position, totalConfirmed),
        bodyHtml:
            VulnwatchEmailLayout.Paragraph("Welcome to Vulnwatch. Your spot is officially reserved.") +
            VulnwatchEmailLayout.Paragraph(
                "We&rsquo;ll notify you the moment early access is available.") +
            VulnwatchEmailLayout.Paragraph(
                "In the meantime, follow us for more updates.", last: true),
        buttonLabel: "Back to Home",
        buttonUrl: branding.HomeUrl,
        // Kept from the pre-redesign template even though the mockup omits it: the referral link is
        // how a recipient moves up the queue, and this mail is the only place they receive it.
        footnote:
            "Want to move up? Share your referral link &mdash; every person who joins with it moves " +
            "you closer to the front:<br>" +
            $"<span style='word-break: break-all;'>{referralLink}</span>");
}
