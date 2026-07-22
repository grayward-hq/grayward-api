using Domain.Enums;

namespace Application.Features.Waitlist;

/// <summary>
/// Message sent to the address owner when someone tries to join the waitlist with an email that is
/// already on it. The public API deliberately returns the same generic response either way to prevent
/// enumeration; this mail is safe because it only ever reaches the real owner of the address (who
/// already knows they signed up), never the party that submitted the form. The copy adapts to the
/// entry's current status so the owner learns where they stand and what, if anything, to do next.
/// </summary>
internal static class WaitlistAlreadyJoinedEmail
{
    public const string Subject = "You're already on the Vulnwatch waitlist";

    /// <param name="status">The existing entry's status.</param>
    /// <param name="livePosition">
    /// Live queue position, only meaningful for a confirmed entry; null otherwise.
    /// </param>
    public static string BuildBody(WaitlistStatus status, long? livePosition)
    {
        var message = status switch
        {
            WaitlistStatus.Pending => @"
                <p style='font-size: 16px; color: #555;'>
                    Good news — this email is <strong>already on the Vulnwatch waitlist</strong>.
                    You joined before, so there's no need to sign up again.
                </p>
                <p style='font-size: 16px; color: #555;'>
                    You just haven't <strong>confirmed your email</strong> yet, so your spot isn't
                    secured. Check your inbox (and spam folder) for the original confirmation email
                    and click the confirmation link to lock in your position.
                </p>",

            WaitlistStatus.EmailConfirmed => $@"
                <p style='font-size: 16px; color: #555;'>
                    This email is <strong>already on the Vulnwatch waitlist</strong> and your email
                    address is <strong>already confirmed</strong> — you're all set, there's nothing
                    more to do.
                </p>
                <p style='font-size: 16px; color: #555;'>
                    You're currently <strong>#{livePosition}</strong> in the queue. Want to move up?
                    Share your referral link — every person who joins with it moves you closer to the
                    front.
                </p>",

            WaitlistStatus.Promoted => @"
                <p style='font-size: 16px; color: #555;'>
                    This email has <strong>already been invited off the waitlist</strong> — you have
                    a Vulnwatch account. Just sign in with this email; there's no need to join the
                    waitlist again.
                </p>",

            _ => @"
                <p style='font-size: 16px; color: #555;'>
                    This email is already associated with the Vulnwatch waitlist.
                </p>",
        };

        return $@"
    <!DOCTYPE html>
    <html>
    <head>
        <meta charset='UTF-8'>
        <title>You're already on the waitlist</title>
    </head>
    <body style='font-family: Arial, sans-serif; background-color: #f9f9f9; padding: 20px;'>
        <div style='max-width: 600px; margin: auto; background: #ffffff; padding: 30px; border-radius: 8px;'>
            <h2 style='color: #333;'>You're already on the list 🎯</h2>
            {message}
            <p style='font-size: 12px; color: #999; margin-top: 40px;'>
                If you didn't just try to join the Vulnwatch waitlist, you can safely ignore this email —
                nothing has changed.
            </p>
        </div>
    </body>
    </html>";
    }
}
