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
        title: "You already have an account",
        preheader: "This email is already registered with Vulnwatch — just sign in.",
        headingLead: "You&rsquo;re already",
        headingAccent: "set up!",
        bodyHtml:
            WaitlistEmailLayout.Paragraph(
                "Someone just tried to join the Vulnwatch waitlist with this email, but it&rsquo;s " +
                "already registered as a full Vulnwatch account.") +
            WaitlistEmailLayout.Paragraph(
                "There&rsquo;s no waitlist to join &mdash; you&rsquo;re already in.") +
            WaitlistEmailLayout.Paragraph(
                "Just sign in with this email to pick up where you left off.", last: true),
        footnote:
            "If this wasn&rsquo;t you, you can safely ignore this email &mdash; " +
            "nothing has changed on your account.");
}
