using System.Security.Cryptography;

namespace Application.Features.Waitlist;

/// <summary>
/// Generates the email-confirmation tokens used by the waitlist join and resend flows. Shared so both
/// produce the same 32-byte, URL-safe (base64url, unpadded) format from one implementation.
/// </summary>
internal static class WaitlistTokens
{
    public static string NewConfirmationToken()
    {
        const int tokenLength = 32;
        var bytes = new byte[tokenLength];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}
