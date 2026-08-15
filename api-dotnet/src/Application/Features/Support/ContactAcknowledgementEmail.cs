using Application.Common.Email;

namespace Application.Features.Support;

/// <summary>
/// Confirmation sent to whoever submits the contact form. Built by
/// <see cref="ContactUsCommandHandler"/>.
/// </summary>
/// <remarks>
/// The internal notification that goes to the support team deliberately does not use this chrome —
/// see <see cref="ContactUsCommandHandler"/>. Only this recipient-facing message does.
/// See <see cref="VulnwatchEmailLayout"/> for the shared chrome.
/// </remarks>
internal static class ContactAcknowledgementEmail
{
    public const string Subject = "We received your message";

    /// <param name="name">Submitter's name, as typed into the form.</param>
    public static string BuildBody(VulnwatchEmailBranding branding, string name) =>
        VulnwatchEmailLayout.Render(
            branding,
            title: "We received your message",
            preheader: "Thanks for reaching out — we'll get back to you shortly.",
            headingLead: "Message",
            headingAccent: "received!",
            bodyHtml:
                VulnwatchEmailLayout.Paragraph(
                    $"Thanks for reaching out, {Escape(name)}.") +
                VulnwatchEmailLayout.Paragraph(
                    "We&rsquo;ve got your message and will get back to you as soon as we can.") +
                VulnwatchEmailLayout.Paragraph(
                    "If your matter is urgent, please call our support line directly.", last: true),
            buttonLabel: "Back to Home",
            buttonUrl: branding.HomeUrl,
            footnote:
                "You&rsquo;re receiving this because you submitted a contact request on our website.");

    /// <summary>
    /// Encodes the name, which is free-text form input interpolated into the message body.
    /// </summary>
    private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value);
}
