using Application.Features.Auth.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace Application.Features.Auth;

public record RefreshTokenCommand(string RefreshToken) : IRequest<Result<AuthResponse>>;

public class RefreshTokenHandler(
    IRefreshTokenRepository refreshTokenRepo,
    UserManager<User> userManager,
    IJwtService jwt,
    IConfiguration config)
    : IRequestHandler<RefreshTokenCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(RefreshTokenCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.RefreshToken))
            return Result<AuthResponse>.Failure(
                Error.Validation("Refresh token is required."));

        var stored = await refreshTokenRepo.GetByToken(cmd.RefreshToken, ct);

        if (stored is null || stored.IsRevoked || stored.IsExpired)
            return Result<AuthResponse>.Failure(
                Error.Unauthorized("Invalid or expired refresh token."));

        var user = await userManager.FindByIdAsync(stored.UserId.ToString());
        if (user is null)
            return Result<AuthResponse>.Failure(
                Error.Unauthorized("User no longer exists."));

        // Rotate: revoke current, issue new pair
        stored.Revoke();
        // await refreshTokenRepo.SaveChangesAsync(ct);

        var rawExpireDays = config["Jwt:RefreshTokenExpiryDays"];
        var parsed = int.TryParse(rawExpireDays, out var days) ? days : 7;
        var expireDays = parsed > 0 ? parsed : 7;

        var newRefreshToken = jwt.GenerateRefreshToken();

        await refreshTokenRepo.AddAsync(
            RefreshToken.Create(user.Id, newRefreshToken, DateTime.UtcNow.AddDays(expireDays)),
            ct);
        await refreshTokenRepo.SaveChangesAsync(ct);

        return Result<AuthResponse>.Success(
            AuthResponse.Create(jwt.GenerateToken(user), newRefreshToken));
    }
}