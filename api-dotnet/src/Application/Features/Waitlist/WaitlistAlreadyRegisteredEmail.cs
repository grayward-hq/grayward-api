namespace Application.Features.Waitlist;

/// <summary>
/// Message sent to the address owner when someone tries to join the waitlist with an email that is
/// already a registered Vulnwatch account. Like <see cref="WaitlistAlreadyJoinedEmail"/>, the public
/// API returns the same generic response regardless, so this reveals nothing to a form-submitting
/// attacker — the mail only reaches the mailbox owner, who already has an account.
/// </summary>
internal static class WaitlistAlreadyRegisteredEmail
{
    public const string Subject = "You already have a Vulnwatch account";

    public static string BuildBody()
    {
        return @"
    <!DOCTYPE html>
    <html>
    <head>
        <meta charset='UTF-8'>
        <title>You already have an account</title>
    </head>
    <body style='font-family: Arial, sans-serif; background-color: #f9f9f9; padding: 20px;'>
        <div style='max-width: 600px; margin: auto; background: #ffffff; padding: 30px; border-radius: 8px;'>
            <h2 style='color: #333;'>You're already set up 🎯</h2>

            <p style='font-size: 16px; color: #555;'>
                Someone just tried to join the Vulnwatch waitlist with this email, but it's
                <strong>already registered as a full Vulnwatch account</strong>. There's no waitlist
                to join — you're already in.
            </p>

            <p style='font-size: 16px; color: #555;'>
                Just sign in with this email to pick up where you left off.
            </p>

            <p style='font-size: 12px; color: #999; margin-top: 40px;'>
                If this wasn't you, you can safely ignore this email — nothing has changed on your account.
            </p>
        </div>
    </body>
    </html>";
    }
}
