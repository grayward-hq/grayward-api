using Application.Common.Email;

namespace Application.Features.Waitlist;

/// <summary>
/// Sent when a waitlist entry is promoted to a full account: the recipient has been invited off the
/// queue and needs to set a password before signing in. Built by
/// <see cref="Commands.PromoteWaitlistHandler"/>.
/// </summary>
/// <remarks>See <see cref="VulnwatchEmailLayout"/> for the shared chrome.</remarks>
internal static class WaitlistInvitationEmail
{
    public const string Subject = "Welcome to Vulnwatch - Set Your Password";

    /// <param name="resetLink">One-time link to the password-set page.</param>
    public static string BuildBody(VulnwatchEmailBranding branding, string resetLink) =>
        VulnwatchEmailLayout.Render(
            branding,
            title: "Welcome to Vulnwatch",
            preheader: "Your waitlist spot has been activated — set your password to sign in.",
            headingLead: "You&rsquo;re",
            headingAccent: "in!",
            bodyHtml:
                VulnwatchEmailLayout.Paragraph(
                    "Your waitlist spot has been activated and your Vulnwatch account is ready.") +
                VulnwatchEmailLayout.Paragraph(
                    "Set a password to sign in and start monitoring your attack surface.", last: true),
            buttonLabel: "Set your password",
            buttonUrl: resetLink,
            // The raw link is kept because this one is time-limited and the recipient cannot
            // request another from the app — a client that strips the button would strand them.
            footnote:
                "Or paste this link into your browser:<br>" +
                $"<span style='word-break: break-all;'>{resetLink}</span>");
}
