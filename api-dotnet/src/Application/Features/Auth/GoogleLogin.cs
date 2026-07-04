using Application.Helpers;
using Application.Features.Auth.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Application.Features.Auth;

public record GoogleLoginCommand(string IdToken, string? IpAddress = null,
    string? UserAgent = null) : IRequest<Result<AuthResponse>>;


public class GoogleLoginHandler(
    UserManager<User> userManager,
    IRefreshTokenRepository refreshTokenRepo,
    IGoogleTokenVerifier googleTokenVerifier,
    IUserService userService,
    IJwtService jwt,
    ILogger<GoogleLoginHandler> logger,
    IConfiguration config) : IRequestHandler<GoogleLoginCommand, Result<AuthResponse>>
{
    public async Task<Result<AuthResponse>> Handle(GoogleLoginCommand cmd, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(cmd.IdToken))
                return Result<AuthResponse>.Failure(Error.Validation("Google id token is required."));

            var verificationResult = await googleTokenVerifier.VerifyIdTokenAsync(cmd.IdToken, ct);
            if (!verificationResult.IsSuccess)
                return Result<AuthResponse>.Failure(verificationResult.Error!);

            var googleUser = verificationResult.Value!;
            if (!googleUser.EmailVerified)
                return Result<AuthResponse>.Failure(Error.Unauthorized("Google account email must be verified."));

            var user = userManager.Users
                .SingleOrDefault(u => u.GoogleId == googleUser.Subject);

            if (user is null)
            {
                user = await userManager.FindByEmailAsync(googleUser.Email);

                if (user is null)
                {
                    user = User.CreateFromGoogle(googleUser.Email, googleUser.Subject, googleUser.Name, googleUser.Picture);
                    var createResult = await userManager.CreateAsync(user);

                    if (!createResult.Succeeded)
                        return Result<AuthResponse>.Failure(Error.Validation(createResult.Errors.First().Description));

                    if (!string.IsNullOrWhiteSpace(googleUser.Picture))
                    {
                        user.UpdateProfile(user.FirstName, user.LastName, profilePictureUrl: googleUser.Picture);

                        var pictureUpdateResult = await userManager.UpdateAsync(user);

                        if (!pictureUpdateResult.Succeeded)
                            return Result<AuthResponse>.Failure(
                                Error.Validation(pictureUpdateResult.Errors.First().Description));
                    }

                    var provisionResult = await userService.ProvisionNewUser(user, ct);

                    if (!provisionResult.IsSuccess)
                        return Result<AuthResponse>.Failure(provisionResult.Error!);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(user.GoogleId) &&
                        !string.Equals(user.GoogleId, googleUser.Subject, StringComparison.Ordinal))
                    {
                        return Result<AuthResponse>.Failure(
                            Error.Conflict("This email is already linked to another Google account."));
                    }

                    user.LinkGoogleAccount(googleUser.Subject);
                    user.ConfirmEmail();
                    user.UpdateEmailAddress(googleUser.Email);

                    var updateResult = await userManager.UpdateAsync(user);
                    if (!updateResult.Succeeded)
                        return Result<AuthResponse>.Failure(Error.Validation(updateResult.Errors.First().Description));
                }
            }
            else
            {
                var shouldUpdate = user.ConfirmEmail();
                shouldUpdate = user.UpdateEmailAddress(googleUser.Email) || shouldUpdate;

                if (shouldUpdate)
                {
                    var updateResult = await userManager.UpdateAsync(user);
                    if (!updateResult.Succeeded)
                        return Result<AuthResponse>.Failure(Error.Validation(updateResult.Errors.First().Description));
                }
            }
            var refreshToken = jwt.GenerateRefreshToken();
            var expireDays = int.Parse(config["Jwt:RefreshTokenExpiryDays"] ?? "7")!;

            var refreshTokenExpiryInDays = DateTime.UtcNow.AddDays(expireDays);

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
        catch (Exception ex)
        {
            logger.LogError(ex, "Google Login Failed for {TokenId}", cmd.IdToken);
            return Result<AuthResponse>.Failure(
                Error.Internal("Google login failed. Please try again."));
        }
    }
}