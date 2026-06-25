using Application.Features.Waitlist.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Application.Features.Waitlist.Commands;

using WaitlistEntity = global::Domain.Entities.Waitlist;

public record JoinWaitlistCommand(string Email, string? CompanyName = null, string? Comments = null) 
    : IRequest<Result<WaitlistResponse>>;

public class JoinWaitlistHandler : IRequestHandler<JoinWaitlistCommand, Result<WaitlistResponse>>
{
    private readonly IWaitlistRepository _waitlistRepo;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly IWaitlistCancellationTokenService _cancellationTokenService;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<JoinWaitlistHandler> _logger;

    public JoinWaitlistHandler(
        IWaitlistRepository waitlistRepo,
        IEmailService emailService,
        IConfiguration config,
        IWaitlistCancellationTokenService cancellationTokenService,
        UserManager<User> userManager,
        ILogger<JoinWaitlistHandler> logger)
    {
        _waitlistRepo = waitlistRepo;
        _emailService = emailService;
        _config = config;
        _cancellationTokenService = cancellationTokenService;
        _userManager = userManager;
        _logger = logger;
    }



    public async Task<Result<WaitlistResponse>> Handle(JoinWaitlistCommand cmd, CancellationToken ct)
    {
        var normalizedEmail = cmd.Email.ToLowerInvariant();

        var genericSuccessResponse = new WaitlistResponse(
            normalizedEmail,
            Position: 0, 
            Status: WaitlistStatus.Pending, 
            CreatedAt: DateTime.UtcNow,
            EmailConfirmed: false
        );

        // Check if email already on waitlist
        var existingWaitlistEntry = await _waitlistRepo.FindByEmail(normalizedEmail, ct);
        if (existingWaitlistEntry is not null)
        {
            _logger.LogInformation("Account enumeration masked: User {email} attempted to join waitlist but already exists.", cmd.Email);
            return Result<WaitlistResponse>.Success(genericSuccessResponse);
        }

        // Check if email already registered as user
        var existingUser = await _userManager.FindByEmailAsync(normalizedEmail);
        if (existingUser is not null)
        {
            _logger.LogInformation("Account enumeration masked: User {email} attempted to join waitlist but already registered.", cmd.Email);
            return Result<WaitlistResponse>.Success(genericSuccessResponse);
        }

        var position = await _waitlistRepo.GetNextPosition(ct);

        var entry = WaitlistEntity.Create(normalizedEmail, cmd.CompanyName, position, cmd.Comments);

        var confirmationToken = GenerateToken();
        entry.GenerateEmailConfirmationToken(confirmationToken);

        try
        {
            await _waitlistRepo.AddAsync(entry, ct);
            await _waitlistRepo.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            var constraintName = GetConstraintName(ex);
            _logger.LogInformation(
                ex,
                "Waitlist join hit unique constraint {constraintName} for {email}.",
                constraintName,
                cmd.Email);

            if (constraintName == "IX_Waitlists_Position")
            {
                return Result<WaitlistResponse>.Failure(
                    Error.Validation("Could not assign waitlist position. Please try again."));
            }

            return Result<WaitlistResponse>.Success(genericSuccessResponse);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to save waitlist entry for {email}.", cmd.Email);
            return Result<WaitlistResponse>.Failure(
                Error.Validation("Could not join waitlist. Please try again."));
        }

        try
        {
            var confirmLink = BuildConfirmationLink(normalizedEmail, confirmationToken);
            var cancellationToken = _cancellationTokenService.GenerateToken(entry.Id, normalizedEmail);
            var cancellationLink = BuildCancellationLink(normalizedEmail, cancellationToken);

            await SendConfirmationEmail(normalizedEmail, position, confirmLink, cancellationLink);
            _logger.LogInformation("Confirmation email sent successfully to {email}", cmd.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send confirmation email to {email}. Aborting registration.", cmd.Email);

            _waitlistRepo.Remove(entry);
            await _waitlistRepo.SaveChangesAsync(ct);

            return Result<WaitlistResponse>.Failure(
                Error.Validation("Could not send confirmation email. Please verify your address and try again."));
        }

        return Result<WaitlistResponse>.Success(
            new WaitlistResponse(
                entry.Email,
                entry.Position,
                entry.Status,
                entry.CreatedAt,
                entry.EmailConfirmed));
    }



    private string GenerateToken()
    {
        const int tokenLength = 32;
        var bytes = new byte[tokenLength];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private string BuildConfirmationLink(string email, string token)
    {
        var baseUrl = _config["FrontendUrl:WaitlistVerify"] ?? _config["FrontendUrl:Base"] ?? "http://localhost:3000";
        return $"{baseUrl}/?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }

    private string BuildCancellationLink(string email, string token)
    {
        var baseUrl = _config["FrontendUrl:WaitlistCancel"] ?? _config["FrontendUrl:Base"] ?? "http://localhost:3000";
        return $"{baseUrl}/?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }

    private async Task SendConfirmationEmail(string email, long position, string confirmLink, string cancellationLink)
    {
        var body = BuildConfirmationEmailBody(email, position, confirmLink, cancellationLink);
        await _emailService.SendAsync(email, "Confirm Your Email - Vulnwatch Waitlist", body);
    }

    private string BuildConfirmationEmailBody(string email, long position, string confirmLink, string cancellationLink)
    {
        return $@"
    <!DOCTYPE html>
    <html>
    <head>
        <meta charset='UTF-8'>
        <title>Confirm Your Email</title>
    </head>
    <body style='font-family: Arial, sans-serif; background-color: #f9f9f9; padding: 20px;'>
        <div style='max-width: 600px; margin: auto; background: #ffffff; padding: 30px; border-radius: 8px;'>
            <h2 style='color: #333;'>Welcome to Vulnwatch! 🎯</h2>

            <p style='font-size: 16px; color: #555;'>
                Thanks for your interest in Vulnwatch. You're currently <strong>#{position}</strong> on our waitlist.
            </p>

            <p style='font-size: 16px; color: #555;'>
                Please confirm your email address to secure your spot:
            </p>

            <div style='text-align: center; margin: 30px 0;'>
                <a href='{confirmLink}' 
                   style='background-color: #4CAF50; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; font-size: 16px;'>
                    Confirm Email
                </a>
            </div>

            <p style='font-size: 14px; color: #999;'>
                Or paste this link in your browser:<br>
                <code style='background-color: #f0f0f0; padding: 5px; display: inline-block;'>{confirmLink}</code>
            </p>

            <p style='font-size: 12px; color: #999; margin-top: 40px;'>
                This confirmation link can be used until your email is confirmed.
            </p>

            <p style='font-size: 12px; color: #999; margin-top: 24px;'>
                If you no longer want to stay on the waitlist, you can remove your spot here:<br>
                <a href='{cancellationLink}' style='color: #777;'>Cancel waitlist spot</a>
            </p>
        </div>
    </body>
    </html>";
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner?.GetType().FullName != "Npgsql.PostgresException") return false;

        return inner.GetType()
            .GetProperty("SqlState")?
            .GetValue(inner) as string == "23505";
    }

    private static string? GetConstraintName(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner?.GetType().FullName != "Npgsql.PostgresException") return null;

        return inner.GetType()
            .GetProperty("ConstraintName")?
            .GetValue(inner) as string;
    }
}
