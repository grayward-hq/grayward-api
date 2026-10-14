using System.Net;
using Application.Features.Auth.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Application.Features.Auth;

public record ForgotPasswordCommand(string Email) : IRequest<Result<MessageResponse>>;

public class ForgotPasswordHandler : IRequestHandler<ForgotPasswordCommand, Result<MessageResponse>>
{
    private readonly UserManager<User> _userManager;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<ForgotPasswordHandler> _logger;


    public ForgotPasswordHandler(UserManager<User> userManager, IEmailService email, IConfiguration config, ILogger<ForgotPasswordHandler> logger)
    {
        _userManager = userManager;
        _email = email;
        _config = config;
        _logger = logger;
    }

    public async Task<Result<MessageResponse>> Handle(ForgotPasswordCommand cmd, CancellationToken ct)
    {
        // Always return the same message so we don't reveal whether the email exists
        const string message = "If this email is registered, a reset link has been sent.";

        var user = await _userManager.FindByEmailAsync(cmd.Email);
        if (user is null)
            return Result<MessageResponse>.Success(MessageResponse.Create(message));

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebUtility.UrlEncode(token);

        var frontendUrl = _config["FrontendUrl:ForgotPassword"];
        var resetLink = $"{frontendUrl}/api/auth/reset-password?email={Uri.EscapeDataString(cmd.Email)}&token={encodedToken}";

        _logger.LogInformation("VERIFICATION LINK: {link}", resetLink);

        var displayName = string.IsNullOrWhiteSpace(user.FirstName)
            ? user.Email!
            : user.FirstName;

        var body = BuildForgotPasswordEmailBody(displayName, resetLink);
        await _email.SendAsync(cmd.Email, "Reset your GrayWard password", body);

        return Result<MessageResponse>.Success(MessageResponse.Create(message));
    }

    private string BuildForgotPasswordEmailBody(string displayName, string resetLink)
    {
        return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Reset your password</title>
</head>
<body style='margin: 0; padding: 0; background-color: #f4f4f5; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif;'>
    <table width='100%' cellpadding='0' cellspacing='0' style='padding: 40px 20px;'>
        <tr>
            <td align='center'>
                <table width='600' cellpadding='0' cellspacing='0' style='max-width: 600px; width: 100%;'>

                    <!-- Header -->
                    <tr>
                        <td style='background-color: #0f172a; padding: 32px 40px; border-radius: 12px 12px 0 0; text-align: center;'>
                            <span style='font-size: 22px; font-weight: 700; color: #ffffff; letter-spacing: -0.5px;'>
                                🔐 VulnWatch
                            </span>
                        </td>
                    </tr>

                    <!-- Body -->
                    <tr>
                        <td style='background-color: #ffffff; padding: 40px; border-left: 1px solid #e4e4e7; border-right: 1px solid #e4e4e7;'>

                            <h1 style='margin: 0 0 8px; font-size: 24px; font-weight: 700; color: #0f172a;'>
                                Reset your password
                            </h1>
                            <p style='margin: 0 0 24px; font-size: 15px; color: #71717a;'>
                                Hi {displayName}, we received a request to reset your password.
                            </p>

                            <p style='margin: 0 0 32px; font-size: 15px; color: #3f3f46; line-height: 1.6;'>
                                Click the button below to choose a new password. This link is valid for
                                <strong style='color: #0f172a;'>60 minutes</strong> and can only be used once.
                            </p>

                            <!-- CTA Button -->
                            <table cellpadding='0' cellspacing='0' style='margin: 0 0 32px;'>
                                <tr>
                                    <td style='background-color: #2563eb; border-radius: 8px;'>
                                        <a href='{resetLink}'
                                           style='display: inline-block; padding: 14px 32px; font-size: 15px;
                                                  font-weight: 600; color: #ffffff; text-decoration: none;
                                                  letter-spacing: 0.2px;'>
                                            Reset Password →
                                        </a>
                                    </td>
                                </tr>
                            </table>

                            <!-- Divider -->
                            <hr style='border: none; border-top: 1px solid #e4e4e7; margin: 0 0 24px;' />

                            <!-- Fallback link -->
                            <p style='margin: 0 0 8px; font-size: 13px; color: #71717a;'>
                                Button not working? Copy and paste this link into your browser:
                            </p>
                            <p style='margin: 0 0 24px; font-size: 12px; color: #2563eb; word-break: break-all;'>
                                {resetLink}
                            </p>

                            <!-- Warning box -->
                            <table cellpadding='0' cellspacing='0' width='100%'>
                                <tr>
                                    <td style='background-color: #fefce8; border: 1px solid #fde047;
                                               border-radius: 8px; padding: 16px;'>
                                        <p style='margin: 0; font-size: 13px; color: #854d0e; line-height: 1.5;'>
                                            ⚠️ <strong>Didn't request this?</strong> Your password will remain unchanged.
                                            If you're concerned about your account security, please contact our support team immediately.
                                        </p>
                                    </td>
                                </tr>
                            </table>

                        </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                        <td style='background-color: #f4f4f5; padding: 24px 40px; border-radius: 0 0 12px 12px;
                                   border: 1px solid #e4e4e7; border-top: none; text-align: center;'>
                            <p style='margin: 0 0 4px; font-size: 12px; color: #a1a1aa;'>
                                VulnWatch — Vulnerability Monitoring Platform
                            </p>
                            <p style='margin: 0; font-size: 12px; color: #a1a1aa;'>
                                This is an automated message, please do not reply.
                            </p>
                        </td>
                    </tr>

                </table>
            </td>
        </tr>
    </table>
</body>
</html>
    ";
    }
}
