using System.Security.Cryptography;
using System.Text;
using Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Application.Services;

/// <summary>
/// HMAC-SHA256 based implementation of <see cref="IWaitlistCancellationTokenService"/>.
///
/// Token format (base64url): {expiryUnixSeconds}.{base64url(hmac)}
/// The HMAC is computed over "{waitlistEntryId}|{normalizedEmail}|{expiry}"
/// using a server-side secret, so the token cannot be forged or replayed
/// for a different entry/email, and cannot be altered without detection.
/// </summary>
public class WaitlistCancellationTokenService : IWaitlistCancellationTokenService
{
    private readonly byte[] _key;
    private readonly TimeSpan _ttl;

    public WaitlistCancellationTokenService(IConfiguration configuration)
    {
        var secret = configuration["Waitlist:CancellationTokenSecret"]
            ?? throw new InvalidOperationException(
                "Waitlist:CancellationTokenSecret must be configured.");

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                "Waitlist:CancellationTokenSecret must not be empty or whitespace.");
        }

        var key = Encoding.UTF8.GetBytes(secret);
        if (key.Length < 32)
        {
            throw new InvalidOperationException(
                "Waitlist:CancellationTokenSecret must be at least 32 bytes when UTF-8 encoded.");
        }

        _key = key;
        _ttl = TimeSpan.FromDays(30);
    }

    public string GenerateToken(Guid waitlistEntryId, string email)
    {
        var expiry = DateTimeOffset.UtcNow.Add(_ttl).ToUnixTimeSeconds();
        var signature = ComputeSignature(waitlistEntryId, email, expiry);

        return $"{expiry}.{Base64UrlEncode(signature)}";
    }

    public bool ValidateToken(string token, Guid waitlistEntryId, string email)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split('.', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!long.TryParse(parts[0], out var expiry))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiry)
        {
            return false;
        }

        byte[] providedSignature;
        try
        {
            providedSignature = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expectedSignature = ComputeSignature(waitlistEntryId, email, expiry);

        return CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature);
    }

    private byte[] ComputeSignature(Guid waitlistEntryId, string email, long expiry)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var message = $"{waitlistEntryId}|{normalizedEmail}|{expiry}";
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 1:
                throw new FormatException("Invalid Base64Url input length.");
            case 2:
                s += "==";
                break;
            case 3:
                s += "=";
                break;
        }

        return Convert.FromBase64String(s);
    }
}
