namespace Application.Features.Auth.DTOs;

public record SessionDto(
    Guid SessionId,       // the RefreshToken row Id
    string DeviceName,
    string? IpAddress,
    DateTime CreatedAt,
    DateTime LastUsedAt,
    DateTime ExpiresAt,
    bool IsCurrent); 