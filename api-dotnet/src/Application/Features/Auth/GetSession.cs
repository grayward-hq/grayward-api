using Application.Features.Auth.DTOs;
using Application.Helpers;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace Application.Features.Auth;
public record GetSessionsQuery()
    : IRequest<Result<IReadOnlyList<SessionDto>>>;

public class GetSessionsHandler(
    IRefreshTokenRepository refreshTokenRepo,
    ICurrentUser currentUser)
    : IRequestHandler<GetSessionsQuery, Result<IReadOnlyList<SessionDto>>>
{
    public async Task<Result<IReadOnlyList<SessionDto>>> Handle(
        GetSessionsQuery query, CancellationToken ct)
    {

        var currentSessionId = currentUser.SessionId;

        var tokens = await refreshTokenRepo.GetActiveByUserId(currentUser.UserId, ct);

        var dtos = tokens
            .Select(t => new SessionDto(
                SessionId:   t.Id,
                DeviceName:  t.DeviceName ?? "Unknown device",
                IpAddress:   t.CreatedByIp,
                CreatedAt:   t.CreatedAt,
                LastUsedAt:  t.LastUsedAt,
                ExpiresAt:   t.ExpiresAt,
                IsCurrent:   currentSessionId.HasValue && t.Id == currentSessionId))
            .ToList();

        return Result<IReadOnlyList<SessionDto>>.Success(dtos);
    }
}

