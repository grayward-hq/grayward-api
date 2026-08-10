using Domain.Enums;

namespace Application.Features.Waitlist;

/// <summary>
/// Message sent to the address owner when someone tries to join the waitlist with an email that is
/// already on it. The public API deliberately returns the same generic response either way to prevent
/// enumeration; this mail is safe because it only ever reaches the real owner of the address (who
/// already knows they signed up), never the party that submitted the form. The copy adapts to the
/// entry's current status so the owner learns where they stand and what, if anything, to do next.
/// </summary>
/// <remarks>See <see cref="WaitlistEmailLayout"/> for the shared chrome.</remarks>
internal static class WaitlistAlreadyJoinedEmail
{
    public const string Subject = "You're already on the Vulnwatch waitlist";

    /// <param name="branding">Optional image assets and links.</param>
    /// <param name="status">The existing entry's status.</param>
    /// <param name="livePosition">
    /// Live queue position, only meaningful for a confirmed entry; null otherwise.
    /// </param>
    /// <param name="totalConfirmed">
    /// Size of the confirmed queue, used as the card's denominator. Null suppresses the card.
    /// </param>
    public static string BuildBody(
        WaitlistEmailBranding branding,
        WaitlistStatus status,
        long? livePosition,
        int? totalConfirmed)
    {
        var body = status switch
        {
            WaitlistStatus.Pending =>
                WaitlistEmailLayout.Paragraph(
                    "Good news &mdash; this email is <strong>already on the Vulnwatch waitlist</strong>. " +
                    "You joined before, so there&rsquo;s no need to sign up again.") +
                WaitlistEmailLayout.Paragraph(
                    "You just haven&rsquo;t <strong>confirmed your email</strong> yet, so your spot isn&rsquo;t " +
                    "secured. Check your inbox (and spam folder) for the original confirmation email " +
                    "and click the confirmation link to lock in your position.", last: true),

            WaitlistStatus.EmailConfirmed =>
                WaitlistEmailLayout.Paragraph(
                    "This email is <strong>already on the Vulnwatch waitlist</strong> and your email " +
                    "address is <strong>already confirmed</strong> &mdash; you&rsquo;re all set, there&rsquo;s " +
                    "nothing more to do.") +
                WaitlistEmailLayout.Paragraph(
                    "We&rsquo;ll notify you the moment early access is available. Want to move up? " +
                    "Share your referral link &mdash; every person who joins with it moves you closer " +
                    "to the front.", last: true),

            WaitlistStatus.Promoted =>
                WaitlistEmailLayout.Paragraph(
                    "This email has <strong>already been invited off the waitlist</strong> &mdash; you have " +
                    "a Vulnwatch account. Just sign in with this email; there&rsquo;s no need to join the " +
                    "waitlist again.", last: true),

            _ =>
                WaitlistEmailLayout.Paragraph(
                    "This email is already associated with the Vulnwatch waitlist.", last: true),
        };

        // The card is only shown for a confirmed entry: pending entries have not claimed a position
        // yet and promoted ones have left the queue, so there is no honest number to display.
        var positionCard = status == WaitlistStatus.EmailConfirmed
                           && livePosition is long position
                           && totalConfirmed is int total
            ? WaitlistEmailLayout.PositionCard(position, total)
            : null;

        return WaitlistEmailLayout.Render(
            branding,
            title: "You're already on the waitlist",
            preheader: "You're already on the Vulnwatch waitlist — here's where you stand.",
            headingLead: "You&rsquo;re already on the",
            headingAccent: "waitlist!",
            bodyHtml: body,
            buttonLabel: "Back to Home",
            buttonUrl: branding.HomeUrl,
            footnote:
                "If you didn&rsquo;t just try to join the Vulnwatch waitlist, you can safely ignore " +
                "this email &mdash; nothing has changed.",
            positionCard: positionCard);
    }
}
