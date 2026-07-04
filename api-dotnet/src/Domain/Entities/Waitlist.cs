using Domain.Enums;

namespace Domain.Entities;

public class Waitlist : EntityBase
{
    // Every user starts their referral ranking at this fixed base, independent of join order.
    public const long InitialReferralPosition = 40;

    public string Email { get; private set; } = default!;
    public string? CompanyName { get; private set; }
    public string? Comments { get; private set; }
    public WaitlistStatus Status { get; private set; } = WaitlistStatus.Pending;
    public long Position { get; private set; }
    public bool EmailConfirmed { get; private set; }
    public string? EmailConfirmationToken { get; private set; }
    public DateTime? EmailConfirmedAt { get; private set; }
    public string? InvitationToken { get; private set; }
    public Guid? PromotedUserId { get; private set; }
    public DateTime? PromotedAt { get; private set; }
    public string? Notes { get; private set; }
    public string ReferralCode { get; private set; } = default!;
    public Guid? ReferredByWaitlistId { get; private set; }
    public int ReferralCount { get; private set; }
    // Referral ranking, independent of the join-order Position. Starts at a fixed base
    // (InitialReferralPosition) for every user and decreases by 1 per referral (floored at 1).
    // Lower = more referrals = higher reward priority.
    public long ReferralPosition { get; private set; }
    public DateTime? LastReferralAt { get; private set; }

    private Waitlist() { }

    public static Waitlist Create(
        string email,
        string? companyName = null,
        long position = 0,
        string? comments = null,
        string? referralCode = null,
        Guid? referredByWaitlistId = null)
        => new()
        {
            Email = email,
            CompanyName = companyName,
            Comments = comments,
            Status = WaitlistStatus.Pending,
            Position = position,
            EmailConfirmed = false,
            ReferralCode = string.IsNullOrWhiteSpace(referralCode) 
                ? GenerateFallbackReferralCode() 
                : referralCode.Trim().ToUpperInvariant(),
            ReferredByWaitlistId = referredByWaitlistId,
            ReferralCount = 0,
            ReferralPosition = InitialReferralPosition,
        };

    public void GenerateEmailConfirmationToken(string token)
    {
        EmailConfirmationToken = token;
        Touch();
    }

    public void ConfirmEmail()
    {
        if (EmailConfirmed)
            return;

        EmailConfirmed = true;
        Status = WaitlistStatus.EmailConfirmed;
        EmailConfirmedAt = DateTime.UtcNow;
        EmailConfirmationToken = null;
        Touch();
    }

    public bool ValidateEmailConfirmationToken(string token)
    {
        return !string.IsNullOrEmpty(EmailConfirmationToken) && EmailConfirmationToken == token && !EmailConfirmed;
    }

    public void GenerateInvitationToken(string token)
    {
        InvitationToken = token;
        Touch();
    }

    public void MarkPromoted(Guid userId)
    {
        Status = WaitlistStatus.Promoted;
        PromotedUserId = userId;
        PromotedAt = DateTime.UtcNow;
        InvitationToken = null;
        Touch();
    }

    public void MarkCancelled()
    {
        Status = WaitlistStatus.Cancelled;
        Touch();
    }

    public void UpdateNotes(string? notes)
    {
        Notes = notes;
        Touch();
    }

    public void UpdateCompanyName(string? companyName)
    {
        CompanyName = companyName;
        Touch();
    }

    public void MarkReferredBy(Guid waitlistId)
    {
        ReferredByWaitlistId = waitlistId;
        Touch();
    }

    private static string GenerateFallbackReferralCode()
        => Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
}
