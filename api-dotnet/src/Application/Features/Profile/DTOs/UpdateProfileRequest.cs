

namespace Application.Features.Profile.DTOs;

public record UpdateProfileRequest(
    string? FirstName,
    string? LastName
);