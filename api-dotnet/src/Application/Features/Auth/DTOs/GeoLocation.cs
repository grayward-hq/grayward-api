namespace Application.Features.Auth.DTOs;

public record GeoLocation(
    string? City,
    string? Region,
    string? Country,
    string? CountryCode);