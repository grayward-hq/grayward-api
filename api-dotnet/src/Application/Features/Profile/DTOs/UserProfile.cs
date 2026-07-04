namespace Application.Features.Profile.DTOs;

public record UserProfileDto(
    Guid Id,
    string Email,
    string? FirstName,
    string? LastName,
    string? ProfilePictureUrl,
    bool EmailConfirmed,
    bool HasGoogleLinked,
    NotificationPreferencesDto? NotificationPreferences,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? ReferralCode = null,
    string? ReferralLink = null
);