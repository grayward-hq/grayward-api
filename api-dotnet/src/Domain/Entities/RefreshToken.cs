

namespace Domain.Entities;

public class RefreshToken : EntityBase
{
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? CreatedByIp { get; private set; }
    public string? DeviceName { get; private set; }   
    public string? UserAgent { get; private set; }    
    public DateTime LastUsedAt { get; private set; }
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt is not null;
    public bool IsActive => !IsRevoked && !IsExpired;

    private RefreshToken() { }

    public static RefreshToken Create(Guid userId, string tokenHash, DateTime expiresAt, string? ip = null,
        string? deviceName = null,
        string? userAgent = null)
        => new()
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedByIp = ip,
            DeviceName = deviceName,
            UserAgent  = userAgent,
            LastUsedAt = DateTime.UtcNow,
        };

    public void Revoke()
    {
        if (IsRevoked) return;
        RevokedAt = DateTime.UtcNow;
    }

    public void RecordUsage()
    {
        LastUsedAt = DateTime.UtcNow;
        Touch();
    }
}
