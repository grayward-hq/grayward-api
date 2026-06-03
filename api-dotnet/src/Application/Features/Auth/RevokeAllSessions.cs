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

public record RevokeAllOtherSessionsCommand()
    : IRequest<Result<MessageResponse>>;

public class RevokeAllOtherSessionsHandler(
    IRefreshTokenRepository refreshTokenRepo,
    ICurrentUser currentUser)
    : IRequestHandler<RevokeAllOtherSessionsCommand, Result<MessageResponse>>
{
    public async Task<Result<MessageResponse>> Handle(
        RevokeAllOtherSessionsCommand cmd, CancellationToken ct)
    {
        var currentSessionId = currentUser.SessionId;

        var tokens = await refreshTokenRepo.GetActiveByUserId(currentUser.UserId, ct);

        var toRevoke = currentSessionId.HasValue
            ? tokens.Where(t => t.Id != currentSessionId.Value).ToList()
            : tokens;

        foreach (var token in toRevoke)
            token.Revoke();

        await refreshTokenRepo.SaveChangesAsync(ct);

        return Result<MessageResponse>.Success(
            MessageResponse.Create($"Revoked {toRevoke.Count} other session(s)."));
    }
}