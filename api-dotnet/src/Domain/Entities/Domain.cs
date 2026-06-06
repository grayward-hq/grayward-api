using Domain.Enums;

namespace Domain.Entities;

public class ScannedDomain : EntityBase
{
    public Guid UserId { get; private set; }
    public string DomainName { get; private set; } = default!;
    public string? VerificationToken { get; private set; }
    public VerificationStatus VerificationStatus { get; private set; }
    public DateTimeOffset? SslCertExpiry { get; private set; }
    public DateTime TokenIssuedAt { get; private set; }
    public User User { get; private set; } = default!;
    public ICollection<Scan> Scans { get; private set; } = new List<Scan>();
    public ICollection<BrandThreat> BrandThreats { get; set; } = [];
    public ICollection<MonitoredEmail> MonitoredEmails { get; set; } = [];

    private ScannedDomain() { }

    public static ScannedDomain Create(Guid userId, string domainName, string? verificationToken = null)
        => new()
        {
            UserId = userId,
            DomainName = domainName,
            VerificationToken = verificationToken,
            VerificationStatus = VerificationStatus.Pending,
            TokenIssuedAt = DateTime.UtcNow,
        };

    public void Verify()
    {
        VerificationStatus = VerificationStatus.Verified;
        VerificationToken = null;
        Touch();
    }

    public void Revoke()
    {
        VerificationStatus = VerificationStatus.Revoked;
        VerificationToken = null;
        Touch();
    }

    public void RegenerateToken(string newTokenHash)
    {
        if (VerificationStatus == VerificationStatus.Verified)
            throw new InvalidOperationException("Cannot regenerate token for a verified domain.");

        VerificationToken = newTokenHash;
        TokenIssuedAt = DateTime.UtcNow;
        Touch();
    }

    public void SetSslCertExpiry(DateTimeOffset? expiry)
    {
        if (SslCertExpiry == expiry)
            return;

        SslCertExpiry = expiry;
        Touch();
    }
}
