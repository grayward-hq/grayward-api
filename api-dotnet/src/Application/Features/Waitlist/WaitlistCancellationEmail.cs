using Application.Common.Email;

namespace Application.Features.Waitlist;

/// <summary>
/// Sent when someone asks for a link to remove an address from the waitlist. Built by
/// <see cref="Commands.RequestWaitlistCancellationHandler"/>.
/// </summary>
/// <remarks>
/// The request is anonymous, so this mail can be triggered by someone who does not own the address.
/// It therefore only ever offers a link to a confirmation page — nothing is cancelled by receiving
/// it — and the copy says so plainly. See <see cref="VulnwatchEmailLayout"/> for the shared chrome.
/// </remarks>
internal static class WaitlistCancellationEmail
{
    public const string Subject = "Cancel your Vulnwatch waitlist spot";

    /// <param name="cancellationLink">Link to the confirmation page; cancels nothing on its own.</param>
    public static string BuildBody(VulnwatchEmailBranding branding, string cancellationLink) =>
        VulnwatchEmailLayout.Render(
            branding,
            title: "Cancel your waitlist spot",
            preheader: "Confirm whether you want to leave the Vulnwatch waitlist.",
            headingLead: "Leaving the",
            headingAccent: "waitlist?",
            bodyHtml:
                VulnwatchEmailLayout.Paragraph(
                    "We received a request to remove this email from the Vulnwatch waitlist.") +
                VulnwatchEmailLayout.Paragraph(
                    "Nothing has changed yet. Open the confirmation page to decide.", last: true),
            buttonLabel: "Review cancellation",
            buttonUrl: cancellationLink,
            footnote:
                "Or paste this link into your browser:<br>" +
                $"<span style='word-break: break-all;'>{cancellationLink}</span><br><br>" +
                "If you didn&rsquo;t request this, ignore this email &mdash; your spot stays active.");
}
