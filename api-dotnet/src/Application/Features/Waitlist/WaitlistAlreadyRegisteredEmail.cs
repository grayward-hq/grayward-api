namespace Application.Features.Waitlist;

/// <summary>
/// Message sent to the address owner when someone tries to join the waitlist with an email that is
/// already a registered Vulnwatch account. Like <see cref="WaitlistAlreadyJoinedEmail"/>, the public
/// API returns the same generic response regardless, so this reveals nothing to a form-submitting
/// attacker — the mail only reaches the mailbox owner, who already has an account.
/// </summary>
/// <remarks>
/// Deliberately carries no queue-position card: the recipient has a full account, so there is no
/// waitlist entry and therefore no position to show. See <see cref="WaitlistEmailLayout"/> for the
/// shared chrome.
/// </remarks>
internal static class WaitlistAlreadyRegisteredEmail
{
    public const string Subject = "You already have a Vulnwatch account";

    public static string BuildBody(WaitlistEmailBranding branding) => WaitlistEmailLayout.Render(
        branding,
        title: "You're already protected",
        preheader: "This email is already linked to an active Vulnwatch account — just sign in.",
        headingLead: "You&rsquo;re already",
        headingAccent: "protected!",
        bodyHtml:
            WaitlistEmailLayout.Paragraph(
                "This email is already linked to an active Vulnwatch account.") +
            WaitlistEmailLayout.Paragraph(
                "You can continue monitoring your attack surface from your existing dashboard.") +
            WaitlistEmailLayout.Paragraph(
                "Simply sign in to access your latest scans, alerts and security insights.", last: true),
        // Falls back to the home page when no dashboard URL is configured, so the recipient always
        // has somewhere to go.
        buttonLabel: branding.DashboardUrl is null ? "Back to Home" : "Go to Dashboard",
        buttonUrl: branding.DashboardUrl ?? branding.HomeUrl,
        belowButton: WaitlistEmailLayout.LinkLine(
            "Forgotten your password?", "Reset it here.", branding.PasswordResetUrl),
        footnote:
            "If this wasn&rsquo;t you, you can safely ignore this email &mdash; " +
            "nothing has changed on your account.");
}
