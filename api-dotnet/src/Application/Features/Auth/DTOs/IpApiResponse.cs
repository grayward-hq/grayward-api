namespace Application.Features.Auth.DTOs;

public sealed record IpApiResponse(
    string? Status,
    string? City,
    string? RegionName,
    string? Country,
    string? CountryCode);