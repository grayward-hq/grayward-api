using Application.Features.Auth.DTOs;

namespace Application.Interfaces;

public interface IGeoLocationService
{
    /// <summary>
    /// Resolves an IP address to a location. Returns null if the IP is
    /// private, loopback, or the lookup fails.
    /// </summary>
    Task<GeoLocation?> GetLocationAsync(string? ipAddress, CancellationToken ct = default);
}

