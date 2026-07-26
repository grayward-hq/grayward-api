namespace Application.Features.Waitlist;

/// <summary>
/// Single source of truth for the "confirm your email" waitlist message, shared by the join flow
/// (<see cref="Commands.JoinWaitlistHandler"/>) and the resend flow
/// (<see cref="Commands.ResendWaitlistConfirmationHandler"/>) so the two mails stay identical.
/// </summary>
internal static class WaitlistConfirmationEmail
{
    public const string Subject = "Confirm Your Email - Vulnwatch Waitlist";

    public static string BuildBody(string confirmLink, string cancellationLink)
    {
        return $@"
    <!DOCTYPE html>
    <html>
    <head>
        <meta charset='UTF-8'>
        <title>Confirm Your Email</title>
    </head>
    <body style='font-family: Arial, sans-serif; background-color: #f9f9f9; padding: 20px;'>
        <div style='max-width: 600px; margin: auto; background: #ffffff; padding: 30px; border-radius: 8px;'>
            <h2 style='color: #333;'>Welcome to Vulnwatch! 🎯</h2>

            <p style='font-size: 16px; color: #555;'>
                Thanks for your interest in Vulnwatch. Confirm your email address to claim your spot
                on the waitlist and unlock your personal referral link:
            </p>

            <div style='text-align: center; margin: 30px 0;'>
                <a href='{confirmLink}'
                   style='background-color: #4CAF50; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; font-size: 16px;'>
                    Confirm Email
                </a>
            </div>

            <p style='font-size: 14px; color: #999;'>
                Or paste this link in your browser:<br>
                <code style='background-color: #f0f0f0; padding: 5px; display: inline-block;'>{confirmLink}</code>
            </p>

            <p style='font-size: 12px; color: #999; margin-top: 40px;'>
                This confirmation link can be used until your email is confirmed. Once confirmed,
                you'll get your waitlist position and a referral link to move up the queue.
            </p>

            <p style='font-size: 12px; color: #999; margin-top: 24px;'>
                If you no longer want to stay on the waitlist, you can remove your spot here:<br>
                <a href='{cancellationLink}' style='color: #777;'>Cancel waitlist spot</a>
            </p>
        </div>
    </body>
    </html>";
    }
}
