namespace Application.Interfaces;

/// <summary>
/// Generates and validates signed, tamper-proof tokens used to prove
/// possession of a waitlist entry (i.e. that the caller owns the email)
/// without requiring an authenticated session.
/// </summary>
public interface IWaitlistCancellationTokenService
{
    /// <summary>
    /// Creates a signed token bound to a specific waitlist entry.
    /// Should be included in the cancellation link sent to the user's email.
    /// </summary>
    string GenerateToken(Guid waitlistEntryId, string email);

    /// <summary>
    /// Validates that the supplied token was produced by this service for the
    /// given waitlist entry / email, has not expired, and has not been tampered with.
    /// </summary>
    bool ValidateToken(string token, Guid waitlistEntryId, string email);
}