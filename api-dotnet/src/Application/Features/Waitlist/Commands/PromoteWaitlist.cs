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

namespace Application.Features.Waitlist.Commands;

public record PromoteWaitlistCommand(
    Guid WaitlistId,
    string? FirstName = null,
    string? LastName = null,
    bool SendInvitationEmail = true)
    : IRequest<Result<PromoteWaitlistResponse>>;

public class PromoteWaitlistHandler : IRequestHandler<PromoteWaitlistCommand, Result<PromoteWaitlistResponse>>
{
    private readonly IWaitlistRepository _waitlistRepo;
    private readonly UserManager<User> _userManager;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<PromoteWaitlistHandler> _logger;

    public PromoteWaitlistHandler(
        IWaitlistRepository waitlistRepo,
        UserManager<User> userManager,
        IEmailService emailService,
        IConfiguration config,
        ILogger<PromoteWaitlistHandler> logger)
    {
        _waitlistRepo = waitlistRepo;
        _userManager = userManager;
        _emailService = emailService;
        _config = config;
        _logger = logger;
    }

    public async Task<Result<PromoteWaitlistResponse>> Handle(PromoteWaitlistCommand cmd, CancellationToken ct)
    {
        // Get waitlist entry
        var entry = await _waitlistRepo.GetById(cmd.WaitlistId, ct);
        if (entry is null)
        {
            return Result<PromoteWaitlistResponse>.Failure(
                Error.NotFound("Waitlist entry not found"));
        }

        if (string.IsNullOrWhiteSpace(entry.Email))
        {
            return Result<PromoteWaitlistResponse>.Failure(Error.Validation("Waitlist entry has no valid email address"));
        }

        // Validate status
        if (entry.Status != WaitlistStatus.EmailConfirmed)
        {
            _logger.LogWarning("Promotion attempted for {email} with status {status}", entry.Email, entry.Status);
            return Result<PromoteWaitlistResponse>.Failure(
                Error.Validation("Cannot promote - email not confirmed"));
        }

        // Check if email already registered
        var existingUser = await _userManager.FindByEmailAsync(entry.Email);
        if (existingUser is not null)
        {
            _logger.LogWarning("Promotion attempted for {email} but user already exists", entry.Email);
            return Result<PromoteWaitlistResponse>.Failure(
                Error.Conflict("Email is already a registered user"));
        }

        // Create user account
        var newUser = User.Create(entry.Email!, cmd.FirstName, cmd.LastName);
        var createResult = await _userManager.CreateAsync(newUser);

        if (!createResult.Succeeded)
        {
            _logger.LogError("Failed to create user from waitlist for {email}: {errors}", 
                entry.Email, string.Join(", ", createResult.Errors.Select(e => e.Description)));
            return Result<PromoteWaitlistResponse>.Failure(
                Error.Validation(createResult.Errors.First().Description));
        }

        // Track whether the identity creation step succeeded for rollback logic
        bool identityUserCreated = true;

        try
        {
            // Confirm email on new user
            var confirmationToken = await _userManager.GenerateEmailConfirmationTokenAsync(newUser);
            var confirmationResult = await _userManager.ConfirmEmailAsync(newUser, confirmationToken);

            if (!confirmationResult.Succeeded)
            {
                var errors = string.Join(", ", confirmationResult.Errors.Select(e => e.Description));
                _logger.LogError("Failed to confirm promoted user email for {email}: {errors}", entry.Email, errors);
                throw new InvalidOperationException("Failed to confirm promoted user email.");
            }

            if (cmd.SendInvitationEmail)
            {
                var resetToken = await _userManager.GeneratePasswordResetTokenAsync(newUser);
                var resetLink = BuildPasswordResetLink(newUser.Email!, resetToken);
                await SendInvitationEmail(newUser.Email!, resetLink);
                _logger.LogInformation("Invitation email sent to {email}", newUser.Email);
            }

            // Update waitlist entry
            entry.MarkPromoted(newUser.Id);
            _waitlistRepo.Update(entry);
            
            // Save waitlist modifications
            await _waitlistRepo.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete waitlist promotion for {email}. Executing compensating rollback.", entry.Email);

            // 🛠️ Compensating Rollback: Clean up the dangling identity record
            if (identityUserCreated)
            {
                try
                {
                    var deleteResult = await _userManager.DeleteAsync(newUser);
                    if (!deleteResult.Succeeded)
                    {
                        _logger.LogCritical("CRITICAL: Rollback failed. Could not delete dangling identity user {email}: {errors}", 
                            entry.Email, string.Join(", ", deleteResult.Errors.Select(e => e.Description)));
                    }
                    else
                    {
                        _logger.LogInformation("Successfully rolled back identity creation for {email}.", entry.Email);
                    }
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogCritical(rollbackEx, "CRITICAL: Exception occurred during identity rollback for {email}.", entry.Email);
                }
            }

            // Rethrow or return failure so the request fails safely
            return Result<PromoteWaitlistResponse>.Failure(
                Error.Validation("Failed to finalize waitlist promotion. Please try again."));
        }

        _logger.LogInformation("User promoted from waitlist: {email} -> {userId}", entry.Email, newUser.Id);

        return Result<PromoteWaitlistResponse>.Success(
            new PromoteWaitlistResponse(
                entry.Id,
                newUser.Id,
                newUser.Email!,
                entry.Status,
                entry.PromotedAt ?? DateTime.UtcNow));
    }

    private string BuildPasswordResetLink(string email, string token)
    {
        var baseUrl = _config["FrontendUrl:PasswordReset"] ?? _config["FrontendUrl:Base"] ?? "http://localhost:3000";
        var encodedToken = Uri.EscapeDataString(token);
        return $"{baseUrl}/?email={Uri.EscapeDataString(email)}&token={encodedToken}";
    }

    private async Task SendInvitationEmail(string email, string resetLink)
    {
        var body = BuildInvitationEmailBody(email, resetLink);
        await _emailService.SendAsync(email, "Welcome to Vulnwatch - Set Your Password", body);
    }

    private string BuildInvitationEmailBody(string email, string resetLink)
    {
        return $@"
    <!DOCTYPE html>
    <html>
    <head>
        <meta charset='UTF-8'>
        <title>Welcome to Vulnwatch</title>
    </head>
    <body style='font-family: Arial, sans-serif; background-color: #f9f9f9; padding: 20px;'>
        <div style='max-width: 600px; margin: auto; background: #ffffff; padding: 30px; border-radius: 8px;'>
            <h2 style='color: #333;'>🎉 Welcome to Vulnwatch!</h2>

            <p style='font-size: 16px; color: #555;'>
                Congratulations! Your waitlist spot has been activated. Your account is now ready to use.
            </p>

            <p style='font-size: 16px; color: #555;'>
                Click the link below to set your password and log in:
            </p>

            <div style='text-align: center; margin: 30px 0;'>
                <a href='{resetLink}' 
                   style='background-color: #4CAF50; color: white; padding: 12px 30px; text-decoration: none; border-radius: 5px; font-size: 16px;'>
                    Set Password & Log In
                </a>
            </div>

            <p style='font-size: 14px; color: #999;'>
                Or paste this link in your browser:<br>
                <code style='background-color: #f0f0f0; padding: 5px; display: inline-block;'>{resetLink}</code>
            </p>

            <p style='font-size: 16px; color: #555;'>
                You can now:
            </p>
            <ul style='font-size: 14px; color: #555;'>
                <li>Register your domains</li>
                <li>Verify domain ownership</li>
                <li>Run security scans</li>
                <li>View detailed security reports</li>
            </ul>

            <p style='font-size: 12px; color: #999; margin-top: 40px;'>
                This link expires in 24 hours. If you didn't request this, you can ignore this email.
            </p>
        </div>
    </body>
    </html>";
    }
}
