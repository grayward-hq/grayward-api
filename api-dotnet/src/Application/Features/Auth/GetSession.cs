using Application.Features.Auth.DTOs;
using Application.Interfaces;
using Domain.Common;
using FluentValidation;
using MediatR;

namespace Application.Features.Auth;
public record GetSessionsQuery()
    : IRequest<Result<IReadOnlyList<SessionDto>>>;

public class GetSessionsHandler(
    IRefreshTokenRepository refreshTokenRepo,
    ICurrentUser currentUser,
    IGeoLocationService geoLocation)
    : IRequestHandler<GetSessionsQuery, Result<IReadOnlyList<SessionDto>>>
{
    public async Task<Result<IReadOnlyList<SessionDto>>> Handle(
        GetSessionsQuery query, CancellationToken ct)
    {

       var currentSessionId = currentUser.SessionId;
        var tokens = await refreshTokenRepo.GetActiveByUserId(currentUser.UserId, ct);

        // Deduplicate IPs so we call the API once per unique IP, not once per session
        var uniqueIps = tokens
            .Select(t => t.CreatedByIp)
            .Where(ip => !string.IsNullOrWhiteSpace(ip))
            .Distinct()
            .ToList();

        var locationMap = new Dictionary<string, GeoLocation?>();
        foreach (var ip in uniqueIps)
        {
            locationMap[ip!] = await geoLocation.GetLocationAsync(ip, ct);
        }

        var dtos = tokens
            .Select(t =>
            {
                GeoLocation? loc = t.CreatedByIp is not null
                    ? locationMap.GetValueOrDefault(t.CreatedByIp)
                    : null;

                return new SessionDto(
                    SessionId:   t.Id,
                    DeviceName:  t.DeviceName ?? "Unknown device",
                    IpAddress:   t.CreatedByIp,
                    Location:    loc is null ? null : new LocationDto(
                                     loc.City,
                                     loc.Region,
                                     loc.Country,
                                     loc.CountryCode),
                    CreatedAt:   t.CreatedAt,
                    LastUsedAt:  t.LastUsedAt,
                    ExpiresAt:   t.ExpiresAt,
                    IsCurrent:   currentSessionId.HasValue && t.Id == currentSessionId);
            })
            .ToList();

        return Result<IReadOnlyList<SessionDto>>.Success(dtos);
    }
}

