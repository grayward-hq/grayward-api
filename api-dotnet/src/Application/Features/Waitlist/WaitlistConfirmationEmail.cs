using Application.Common.Email;
namespace Application.Features.Waitlist;

/// <summary>
/// Single source of truth for the "verify your email" waitlist message, shared by the join flow
/// (<see cref="Commands.JoinWaitlistHandler"/>) and the resend flow
/// (<see cref="Commands.ResendWaitlistConfirmationHandler"/>) so the two mails stay identical.
/// </summary>
internal static class WaitlistConfirmationEmail
{
    public const string Subject = "Confirm Your Email - Vulnwatch Waitlist";

    public static string BuildBody(
        VulnwatchEmailBranding branding,
        string confirmLink,
        string cancellationLink) => VulnwatchEmailLayout.Render(
        branding,
        title: "You're almost in",
        preheader: "Verify your email address to secure your Vulnwatch waitlist spot.",
        headingLead: "You&rsquo;re",
        headingAccent: "almost in",
        // The envelope pose rather than the default thumbs-up: this is the one mail still waiting on
        // an action from the recipient.
        mascot: "vulnwatch-mascot-envelope.png",
        bodyHtml:
            VulnwatchEmailLayout.Paragraph("Thanks for joining the Vulnwatch waitlist.") +
            VulnwatchEmailLayout.Paragraph(
                "We need you to verify your email address to secure your spot and start protecting " +
                "what matters.", last: true),
        buttonLabel: "Verify my email",
        buttonUrl: confirmLink,
        infoCard: VulnwatchEmailLayout.InfoCard(
            branding,
            "Why verify?",
            "Verifying your email helps us keep your account secure and ensures you receive " +
            "important alerts about your attack surface."),
        // Kept from the pre-redesign template even though the mockup omits them. The confirmation
        // link is the whole point of the mail and must survive an image-blocking or button-stripping
        // client, and the cancellation link is the recipient's only way out of the queue — dropping
        // it would strand anyone who wants off the list.
        footnote:
            "Or paste this link into your browser:<br>" +
            $"<span style='word-break: break-all;'>{confirmLink}</span><br><br>" +
            "This link works until your email is confirmed. Once confirmed, you&rsquo;ll get your " +
            "waitlist position and a referral link to move up the queue.<br><br>" +
            "No longer want your spot? " +
            $"<a href='{cancellationLink}' style='color: #9CA3AF;'>Cancel waitlist spot</a>.");
}
