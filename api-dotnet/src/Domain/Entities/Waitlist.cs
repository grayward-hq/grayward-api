using Domain.Enums;

namespace Domain.Entities;

public class Waitlist : EntityBase
{
    public string Email { get; private set; } = default!;
    public string? CompanyName { get; private set; }
    public WaitlistStatus Status { get; private set; } = WaitlistStatus.Pending;
    public long Position { get; private set; }
    public bool EmailConfirmed { get; private set; }
    public string? EmailConfirmationToken { get; private set; }
    public DateTime? EmailConfirmedAt { get; private set; }
    public string? InvitationToken { get; private set; }
    public Guid? PromotedUserId { get; private set; }
    public DateTime? PromotedAt { get; private set; }
    public string? Notes { get; private set; }

    private Waitlist() { }

    public static Waitlist Create(string email, string? companyName = null, long position = 0)
        => new()
        {
            Email = email,
            CompanyName = companyName,
            Status = WaitlistStatus.Pending,
            Position = position,
            EmailConfirmed = false,
        };

    public void SetPosition(long position)
    {
        Position = position;
        Touch();
    }

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
}
