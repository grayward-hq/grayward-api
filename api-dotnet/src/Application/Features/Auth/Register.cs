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
    IEmailService email,
    IConfiguration config) : IRequestHandler<RegisterCommand, Result<MessageResponse>>
{

    public async Task<Result<MessageResponse>> Handle(RegisterCommand cmd, CancellationToken ct)
    {
        var existing = await userManager.FindByEmailAsync(cmd.Email);
        if (existing is not null)
            return Result<MessageResponse>.Failure(Error.Conflict("Email is already registered."));

        var user = User.Create(cmd.Email, cmd.FirstName, cmd.LastName);
        var result = await userManager.CreateAsync(user, cmd.Password);

        if (!result.Succeeded)
            return Result<MessageResponse>.Failure(Error.Validation(result.Errors.First().Description));

        await TryCreateDefaultPrefsAsync(user.Id, ct);

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

        var body = BuildVerificationEmailBody(displayName, verificationLink);

        await email.SendAsync(user.Email!, "Verify Your Email", body);

        return Result<MessageResponse>.Success(MessageResponse.Create("Registration successful. Verification link has been sent to your email."));
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

    private string BuildVerificationEmailBody(string userName, string verificationLink)
    {
        return $@"
    <!DOCTYPE html>
    <html>
    <head>
        <meta charset='UTF-8'>
        <title>Verify your email</title>
    </head>
    <body style='font-family: Arial, sans-serif; background-color: #f9f9f9; padding: 20px;'>
        <div style='max-width: 600px; margin: auto; background: #ffffff; padding: 30px; border-radius: 8px;'>
            
            <h2 style='color: #333;'>Welcome, {userName} 👋</h2>

            <p style='font-size: 16px; color: #555;'>
                Thanks for signing up. Please confirm your email address to activate your account.
            </p>

            <p style='font-size: 16px; color: #555;'>
                Click the button below to verify your email:
            </p>

            <div style='text-align: center; margin: 30px 0;'>
                <a href='{verificationLink}' 
                style='background-color: #4CAF50; color: white; padding: 12px 24px; 
                        text-decoration: none; border-radius: 5px; display: inline-block;'>
                    Verify Email
                </a>
            </div>

            <p style='font-size: 14px; color: #777;'>
                If the button doesn’t work, copy and paste this link into your browser:
            </p>

            <p style='font-size: 12px; color: #999; word-break: break-all;'>
                {verificationLink}
            </p>

            <hr style='margin-top: 30px;' />

            <p style='font-size: 12px; color: #aaa;'>
                If you didn’t create this account, you can safely ignore this email.
            </p>

        </div>
    </body>
    </html>
    ";
    }
}
