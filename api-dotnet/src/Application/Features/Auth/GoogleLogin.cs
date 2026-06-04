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

public class GoogleLoginHandler : IRequestHandler<GoogleLoginCommand, Result<AuthResponse>>
{
    private readonly UserManager<User> _userManager;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly INotificationPreferencesRepository _notifPrefs;
    private readonly IGoogleTokenVerifier _googleTokenVerifier;
    private readonly IJwtService _jwt;
    private readonly IConfiguration _config;
    private readonly ILogger<GoogleLoginHandler> _logger;

    public GoogleLoginHandler(
        UserManager<User> userManager,
        IRefreshTokenRepository refreshTokenRepo,
        INotificationPreferencesRepository notifPrefs,
        IGoogleTokenVerifier googleTokenVerifier,
        IJwtService jwt,
        IConfiguration config,
        ILogger<GoogleLoginHandler> logger)
    {
        _userManager = userManager;
        _refreshTokenRepo = refreshTokenRepo;
        _notifPrefs = notifPrefs;
        _googleTokenVerifier = googleTokenVerifier;
        _jwt = jwt;
        _config = config;
        _logger = logger;
    }

    public async Task<Result<AuthResponse>> Handle(GoogleLoginCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.IdToken))
            return Result<AuthResponse>.Failure(Error.Validation("Google id token is required."));

        var verificationResult = await _googleTokenVerifier.VerifyIdTokenAsync(cmd.IdToken, ct);
        if (!verificationResult.IsSuccess)
            return Result<AuthResponse>.Failure(verificationResult.Error!);

        var googleUser = verificationResult.Value!;
        if (!googleUser.EmailVerified)
            return Result<AuthResponse>.Failure(Error.Unauthorized("Google account email must be verified."));

        var user = _userManager.Users
            .SingleOrDefault(u => u.GoogleId == googleUser.Subject);

        if (user is null)
        {
            user = await _userManager.FindByEmailAsync(googleUser.Email);

            if (user is null)
            {
                user = User.CreateFromGoogle(googleUser.Email, googleUser.Subject, googleUser.Name, googleUser.Picture);
                var createResult = await _userManager.CreateAsync(user);

                if (!createResult.Succeeded)
                    return Result<AuthResponse>.Failure(Error.Validation(createResult.Errors.First().Description));

                if (!string.IsNullOrWhiteSpace(googleUser.Picture))
                {
                    user.UpdateProfile(user.FirstName, user.LastName, profilePictureUrl: googleUser.Picture);

                    var pictureUpdateResult = await _userManager.UpdateAsync(user);

                    if (!pictureUpdateResult.Succeeded)
                        return Result<AuthResponse>.Failure(
                            Error.Validation(pictureUpdateResult.Errors.First().Description));
                }

                await TryCreateDefaultPrefsAsync(user.Id, ct);
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

                var updateResult = await _userManager.UpdateAsync(user);
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
                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                    return Result<AuthResponse>.Failure(Error.Validation(updateResult.Errors.First().Description));
            }
        }
        var refreshToken = _jwt.GenerateRefreshToken();
        var expireDays = int.Parse(_config["Jwt:RefreshTokenExpiryDays"] ?? "7")!;

        var refreshTokenExpiryInDays = DateTime.UtcNow.AddDays(expireDays);

        var deviceName = DeviceNameParser.Parse(cmd.UserAgent);

        var refreshTokenEntity = RefreshToken.Create(
                user.Id,
                refreshToken,
                refreshTokenExpiryInDays,
                ip: cmd.IpAddress,
                deviceName: deviceName,
                userAgent: cmd.UserAgent);

        await _refreshTokenRepo.AddAsync(refreshTokenEntity, ct);
        await _refreshTokenRepo.SaveChangesAsync(ct);

        var accessToken = _jwt.GenerateToken(user, sessionId: refreshTokenEntity.Id);

        return Result<AuthResponse>.Success(AuthResponse.Create(accessToken, refreshToken));
    }

    private async Task TryCreateDefaultPrefsAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var prefs = NotificationPreferences.Create(userId, emailAlerts: true);
            await _notifPrefs.AddAsync(prefs, ct);
            await _notifPrefs.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException?.GetType().FullName == "Npgsql.PostgresException" &&
                  ex.InnerException.GetType().GetProperty("SqlState")?.GetValue(ex.InnerException) as string == "23505")
        {
            // Unique constraint on UserId — concurrent insert already seeded prefs.
            // The user has default preferences either way; swallow and continue.
        }
    }
}