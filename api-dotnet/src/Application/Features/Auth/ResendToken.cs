using Application.Common.Email;
using MediatR;
using Domain.Common;
using Domain.Entities;
using Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Application.Features.Auth.DTOs;
using System.Net;

namespace Application.Features.Auth;

public record ResendVerificationCommand(string Email) : IRequest<Result<MessageResponse>>;

public class ResendVerificationHandler(
    UserManager<User> userManager,
    IEmailService email,
    IConfiguration config,
    ILogger<ResendVerificationHandler> logger)
    : IRequestHandler<ResendVerificationCommand, Result<MessageResponse>>
{
    private const int CooldownMinutes = 2;

    public async Task<Result<MessageResponse>> Handle(ResendVerificationCommand cmd, CancellationToken ct)
    {
        const string message = "If this email is registered, a reset link has been sent.";

        var user = await userManager.FindByEmailAsync(cmd.Email);

        if (user is null)
            return Result<MessageResponse>.Success(MessageResponse.Create(message));

        if (user.EmailConfirmed)
            return Result<MessageResponse>.Failure(Error.Conflict("Email is already verified."));

        var verificationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);

        var encodedToken = WebUtility.UrlEncode(verificationToken);
        var verificationLink = $"{config["FrontendUrl:Verify"]!}/?userId={user.Id}&token={encodedToken}";

        logger.LogInformation("RESEND VERIFICATION LINK: {link}", verificationLink);

        await userManager.UpdateAsync(user);

        var body = AccountVerificationEmail.BuildBody(
            VulnwatchEmailBranding.From(config), user.UserName!, verificationLink, isResend: true);
        await email.SendAsync(user.Email!, AccountVerificationEmail.Subject, body);

        return Result<MessageResponse>.Success(MessageResponse.Create(message));

    }

}
