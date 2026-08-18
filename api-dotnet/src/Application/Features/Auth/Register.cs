using Application.Common.Email;
using System.Net;
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

public record RegisterCommand(string Email, string Password, string? FirstName = null, string? LastName = null, string? OriginUrl = null) : IRequest<Result<MessageResponse>>;

public class RegisterHandler(
    UserManager<User> userManager,
    INotificationPreferencesRepository notifPrefs,
    IUserService userService,
    IEmailService email,
    IConfiguration config,
    ILogger<RegisterHandler> logger) : IRequestHandler<RegisterCommand, Result<MessageResponse>>
{
    public async Task<Result<MessageResponse>> Handle(RegisterCommand cmd, CancellationToken ct)
    {
        try
        {
            var existing = await userManager.FindByEmailAsync(cmd.Email);
            if (existing is not null)
                return Result<MessageResponse>.Failure(Error.Conflict("Email is already registered."));

            var user = User.Create(cmd.Email, cmd.FirstName, cmd.LastName);
            var result = await userManager.CreateAsync(user, cmd.Password);

            if (!result.Succeeded)
                return Result<MessageResponse>.Failure(Error.Validation(result.Errors.First().Description));

            var provisionResult = await userService.ProvisionNewUser(user, ct);

            if (!provisionResult.IsSuccess)
                return Result<MessageResponse>.Failure(provisionResult.Error!);

            var verificationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);

            var encodedToken = WebUtility.UrlEncode(verificationToken);

            var baseUrl = !string.IsNullOrWhiteSpace(cmd.OriginUrl)
                ? cmd.OriginUrl
                : config["FrontendUrl:Verify"];


            var verificationLink = $"{baseUrl}/?userId={user.Id}&token={encodedToken}";

            // logger.LogInformation("VERIFICATION LINK: {link}", verificationLink);

            var displayName = string.IsNullOrWhiteSpace(user.FirstName)
                ? user.Email!
                : user.FirstName;

            var body = AccountVerificationEmail.BuildBody(
                VulnwatchEmailBranding.From(config), displayName, verificationLink);

            await email.SendAsync(user.Email!, AccountVerificationEmail.Subject, body);

            return Result<MessageResponse>.Success(MessageResponse.Create("Registration successful. Verification link has been sent to your email."));

        }
        catch (Exception ex)
        {
            logger.LogError(ex, "User Registration Failed for {Email}", cmd.Email);
            return Result<MessageResponse>.Failure(
                Error.Internal("User registration failed. Please try again."));
        }
    }

    private async Task TryCreateDefaultPrefsAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var prefs = NotificationPreferences.Create(userId, emailAlerts: true);
            await notifPrefs.AddAsync(prefs, ct);
            await notifPrefs.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException?.GetType().FullName == "Npgsql.PostgresException" &&
                ex.InnerException.GetType().GetProperty("SqlState")?.GetValue(ex.InnerException) as string == "23505")
        {
            // Already seeded — no-op
        }
    }

}
