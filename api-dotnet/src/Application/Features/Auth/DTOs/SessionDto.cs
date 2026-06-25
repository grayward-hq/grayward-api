namespace Application.Features.Auth.DTOs;

public record LocationDto(
    string? City,
    string? Region,
    string? Country,
    string? CountryCode);

public record SessionDto(
    Guid SessionId,
    string DeviceName,
    string? IpAddress,
    LocationDto? Location, 
    DateTime CreatedAt,
    DateTime LastUsedAt,
    DateTime ExpiresAt,
    bool IsCurrent); 