using System.Net;
using System.Net.Http.Json;
using Application.Features.Auth.DTOs;
using Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public sealed class GeoLocationService(
    HttpClient httpClient,
    ILogger<GeoLocationService> logger) : IGeoLocationService
{
    // ip-api.com free tier: 45 req/min, no key needed
    private static readonly HashSet<string> _privateRanges =
        ["127.0.0.1", "::1", "localhost"];

    public async Task<GeoLocation?> GetLocationAsync(string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || _privateRanges.Contains(ipAddress))
            return null;

        // Reject non-IPs and reserved ranges immediately
        if (!IPAddress.TryParse(ipAddress, out var parsed)
            || IPAddress.IsLoopback(parsed)
            || IsPrivateIp(parsed))
            return null;

        try
        {
            var response = await httpClient.GetFromJsonAsync<IpApiResponse>(
                $"json/{ipAddress}?fields=status,city,regionName,country,countryCode",
                ct);

            if (response is null || response.Status != "success")
                return null;

            return new GeoLocation(response.City, response.RegionName, response.Country, response.CountryCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GeoIP lookup failed for {Ip}", ipAddress);
            return null;    // never hard-fail the sessions endpoint over a GeoIP miss
        }
    }

    private static bool IsPrivateIp(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return bytes[0] switch
        {
            10 => true,
            172 => bytes[1] >= 16 && bytes[1] <= 31,
            192 => bytes[1] == 168,
            _ => false
        };
    }


}