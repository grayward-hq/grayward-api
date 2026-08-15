using Application.Common.Email;

namespace Application.Features.Auth;

/// <summary>
/// The "verify your email" message for account registration, shared by the register and resend
/// flows so the two cannot drift apart — they previously carried near-identical copies of the same
/// HTML in two files.
/// </summary>
/// <remarks>See <see cref="VulnwatchEmailLayout"/> for the shared chrome.</remarks>
internal static class AccountVerificationEmail
{
    public const string Subject = "Verify Your Email";

    /// <param name="displayName">First name where known, else the address.</param>
    /// <param name="verificationLink">One-time verification link.</param>
    /// <param name="isResend">
    /// True when sent from the resend flow, which explains why a second link arrived.
    /// </param>
    public static string BuildBody(
        VulnwatchEmailBranding branding,
        string displayName,
        string verificationLink,
        bool isResend = false)
    {
        var opening = isResend
            ? VulnwatchEmailLayout.Paragraph(
                $"Here&rsquo;s a new verification link, {Escape(displayName)}. The previous one may " +
                "have expired or never arrived.")
            : VulnwatchEmailLayout.Paragraph(
                $"Welcome to Vulnwatch, {Escape(displayName)}. Verify your email address to finish " +
                "setting up your account.");

        return VulnwatchEmailLayout.Render(
            branding,
            title: "Verify your email",
            preheader: "Verify your email address to finish setting up your Vulnwatch account.",
            headingLead: "You&rsquo;re",
            headingAccent: "almost in",
            // The envelope pose: like the waitlist confirmation, this mail is waiting on an action
            // from the recipient.
            mascot: "vulnwatch-mascot-envelope.png",
            bodyHtml:
                opening +
                VulnwatchEmailLayout.Paragraph(
                    "It only takes a moment, and it keeps your account secure.", last: true),
            buttonLabel: "Verify my email",
            buttonUrl: verificationLink,
            infoCard: VulnwatchEmailLayout.InfoCard(
                branding,
                "Why verify?",
                "Verifying your email helps us keep your account secure and ensures you receive " +
                "important alerts about your attack surface."),
            // Kept so the link survives a client that strips or mangles the button — without it a
            // failed button leaves the recipient with no way to finish signing up.
            footnote:
                "Or paste this link into your browser:<br>" +
                $"<span style='word-break: break-all;'>{verificationLink}</span><br><br>" +
                "If you didn&rsquo;t create a Vulnwatch account, you can safely ignore this email.");
    }

    /// <summary>
    /// Encodes the display name, which comes from user-supplied registration input and would
    /// otherwise be interpolated straight into the message body.
    /// </summary>
    private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value);
}
