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
public record RevokeSessionCommand(Guid SessionId) : IRequest<Result<MessageResponse>>;

public class RevokeSessionHandler(
    IRefreshTokenRepository refreshTokenRepo,
    ICurrentUser currentUser)
    : IRequestHandler<RevokeSessionCommand, Result<MessageResponse>>
{
    public async Task<Result<MessageResponse>> Handle(
        RevokeSessionCommand cmd, CancellationToken ct)
    {
        var token = await refreshTokenRepo.GetActiveById(
            cmd.SessionId, currentUser.UserId, ct);

        if (token is null)
            return Result<MessageResponse>.Failure(
                Error.NotFound("Session not found."));

        token.Revoke();
        await refreshTokenRepo.SaveChangesAsync(ct);

        return Result<MessageResponse>.Success(
            MessageResponse.Create("Session revoked successfully."));
    }
}
