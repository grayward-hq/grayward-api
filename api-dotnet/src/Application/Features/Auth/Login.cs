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

public record LoginCommand(string Email, string Password, string? IpAddress = null,
    string? UserAgent = null) : IRequest<Result<AuthResponse>>;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email must be a valid email address.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}

public class LoginHandler(
    UserManager<User> userManager,
    IRefreshTokenRepository refreshTokenRepo,
    IConfiguration config,
    IJwtService jwt) : IRequestHandler<LoginCommand, Result<AuthResponse>>
{

    public async Task<Result<AuthResponse>> Handle(LoginCommand cmd, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(cmd.Email);
        if (user is null || !await userManager.CheckPasswordAsync(user, cmd.Password))
            return Result<AuthResponse>.Failure(Error.Unauthorized("Invalid email or password."));

        if (!user.EmailConfirmed)
            return Result<AuthResponse>.Failure(Error.Forbidden("Your account has not been verified."));


        var refreshToken = jwt.GenerateRefreshToken();

        var refreshTokenExpiryInDays = DateTime.UtcNow.AddMinutes(int.Parse(config["Jwt:RefreshTokenExpiryDays"]!));

        var deviceName = DeviceNameParser.Parse(cmd.UserAgent);

        var refreshTokenEntity = RefreshToken.Create(
                user.Id,
                refreshToken,
                refreshTokenExpiryInDays,
                ip: cmd.IpAddress,
                deviceName: deviceName,
                userAgent: cmd.UserAgent);

        await refreshTokenRepo.AddAsync(refreshTokenEntity, ct);
        await refreshTokenRepo.SaveChangesAsync(ct);

        var accessToken = jwt.GenerateToken(user, sessionId: refreshTokenEntity.Id);

        return Result<AuthResponse>.Success(AuthResponse.Create(accessToken, refreshToken));
    }
}