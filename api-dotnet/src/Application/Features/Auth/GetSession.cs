using Application.Features.Auth.DTOs;
using Application.Interfaces;
using Domain.Common;
using MediatR;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Auth;

public record GetSessionsQuery() : IRequest<Result<IReadOnlyList<SessionDto>>>;

public class GetSessionsHandler(
    IRefreshTokenRepository refreshTokenRepo,
    ICurrentUser currentUser,
    IGeoLocationService geoLocation,
    IMemoryCache cache)
    : IRequestHandler<GetSessionsQuery, Result<IReadOnlyList<SessionDto>>>
{
    private static readonly TimeSpan GeoCacheTtl = TimeSpan.FromHours(24);

    public async Task<Result<IReadOnlyList<SessionDto>>> Handle(
        GetSessionsQuery query, CancellationToken ct)
    {
        var currentSessionId = currentUser.SessionId;
        var tokens = await refreshTokenRepo.GetActiveByUserId(currentUser.UserId, ct);

        var uniqueIps = tokens
            .Select(t => t.CreatedByIp)
            .Where(ip => !string.IsNullOrWhiteSpace(ip))
            .Distinct()
            .ToList();

        // Separate IPs into cache hits and misses in one pass
        var locationMap = new Dictionary<string, GeoLocation?>();
        var missedIps = new List<string>();

        foreach (var ip in uniqueIps)
        {
            if (cache.TryGetValue(CacheKey(ip!), out GeoLocation? cached))
                locationMap[ip!] = cached;
            else
                missedIps.Add(ip!);
        }

        // Fetch all cache misses in parallel
        if (missedIps.Count > 0)
        {
            var fetchTasks = missedIps
                .Select(async ip =>
                {
                    var location = await geoLocation.GetLocationAsync(ip, ct);

                    cache.Set(CacheKey(ip), location, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = GeoCacheTtl,
                        // Don't let GeoIP entries crowd out more important cache entries
                        Size = 1,
                        Priority = CacheItemPriority.Low,
                    });

                    return (ip, location);
                });

            var results = await Task.WhenAll(fetchTasks);

            foreach (var (ip, location) in results)
                locationMap[ip] = location;
        }

        var dtos = tokens
            .Select(t =>
            {
                var loc = t.CreatedByIp is not null
                    ? locationMap.GetValueOrDefault(t.CreatedByIp)
                    : null;

                return new SessionDto(
                    SessionId:  t.Id,
                    DeviceName: t.DeviceName ?? "Unknown device",
                    IpAddress:  t.CreatedByIp,
                    Location:   loc is null ? null : new LocationDto(
                                    loc.City,
                                    loc.Region,
                                    loc.Country,
                                    loc.CountryCode),
                    CreatedAt:  t.CreatedAt,
                    LastUsedAt: t.LastUsedAt,
                    ExpiresAt:  t.ExpiresAt,
                    IsCurrent:  currentSessionId.HasValue && t.Id == currentSessionId);
            })
            .ToList();

        return Result<IReadOnlyList<SessionDto>>.Success(dtos);
    }

    private static string CacheKey(string ip) => $"geoip:{ip}";
}