using Application.Features.Auth;
using Application.Features.Auth.DTOs;
using Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Web.Extensions;

namespace Web.Controllers;

[EnableRateLimiting(RateLimitExtensions.AuthPolicy)]
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator) => _mediator = mediator;


    /// <summary>Registers a new user account.</summary>
    /// <param name="request">User registration details (email, password, first/last name).</param>
    /// <response code="200">Registration successful.</response>
    /// <response code="400">Validation failed or user already exists.</response>
    [HttpPost("register")]
    public async Task<ActionResult<Result<MessageResponse>>> Register(RegisterRequest request)
    {
        var origin = GetClientOrigin();
        var result = await _mediator.Send(new RegisterCommand(request.Email, request.Password, request.FirstName, request.LastName));
        return result.ToHttpResponse(this);
    }

    /// <summary>Verifies a user's email using a verification token.</summary>
    /// <param name="userId">User identifier.</param>
    /// <param name="token">Verification token sent to user email.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Email successfully verified.</response>
    /// <response code="400">Invalid or expired token.</response>
    [HttpGet("verify")]
    public async Task<ActionResult<Result<MessageResponse>>> VerifyToken(
        [FromQuery] string userId,
        [FromQuery] string token,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new VerifyTokenCommand(userId, token), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>Resends email verification link.</summary>
    /// <param name="request">Email address of the user.</param>
    /// <response code="200">Verification email resent successfully.</response>
    /// <response code="400">Invalid request or user not found.</response>
    [HttpPost("resend")]
    public async Task<ActionResult<Result<MessageResponse>>> ResendVerification(ResendTokenRequest request)
    {
        var result = await _mediator.Send(new ResendVerificationCommand(request.Email));
        return result.ToHttpResponse(this);
    }

    /// <summary>Log in to an existing user account</summary>
    /// <response code="200">Login successful — authentication token returned</response>
    /// <response code="400">Invalid email or password</response>
    [HttpPost("login")]
    public async Task<ActionResult<Result<AuthResponse>>> Login(LoginRequest request)
    {
        var result = await _mediator.Send(new LoginCommand(request.Email, request.Password));
        return result.ToHttpResponse(this);
    }

    /// <summary>Refreshes an expired access token using a refresh token.</summary>
    /// <param name="request">Refresh token request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">New authentication tokens returned.</response>
    /// <response code="401">Invalid or expired refresh token.</response>
    [HttpPost("refresh-token")]
    public async Task<ActionResult<Result<AuthResponse>>> RefreshToken(
        RefreshTokenRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new RefreshTokenCommand(request.RefreshToken), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>Authenticates a user using Google Sign-In.</summary>
    /// <param name="request">Google ID token.</param>
    /// <response code="200">Authentication successful.</response>
    /// <response code="400">Invalid Google token.</response>
    [HttpPost("google")]
    public async Task<ActionResult<Result<AuthResponse>>> GoogleLogin(GoogleLoginRequest request)
    {
        var result = await _mediator.Send(new GoogleLoginCommand(request.IdToken));
        return result.ToHttpResponse(this);
    }

    /// <summary>Changes the password of the currently authenticated user.</summary>
    /// <param name="request">Current and new password details.</param>
    /// <response code="200">Password changed successfully.</response>
    /// <response code="400">Invalid current password or validation error.</response>
    /// <response code="401">User is not authenticated.</response>
    [Authorize]
    [HttpPost("change-password")]
    public async Task<ActionResult<Result<MessageResponse>>> ChangePassword(ChangePasswordRequest request)
    {
        var result = await _mediator.Send(new ChangePasswordCommand(request.CurrentPassword, request.NewPassword, request.ConfirmNewPassword));
        return result.ToHttpResponse(this);
    }

    /// <summary>Sends a password reset email to the user.</summary>
    /// <param name="request">User email address.</param>
    /// <response code="200">Password reset email sent.</response>
    /// <response code="400">User not found or invalid request.</response>
    [HttpPost("forgot-password")]
    public async Task<ActionResult<Result<MessageResponse>>> ForgotPassword(ForgotPasswordRequest request)
    {
        var result = await _mediator.Send(new ForgotPasswordCommand(request.Email));
        return result.ToHttpResponse(this);
    }

    /// <summary>Resets a user's password using a valid reset token.</summary>
    /// <param name="request">Email, reset token, and new password.</param>
    /// <response code="200">Password reset successful.</response>
    /// <response code="400">Invalid or expired token.</response>
    [HttpPost("reset-password")]
    public async Task<ActionResult<Result<MessageResponse>>> ResetPassword(ResetPasswordRequest request)
    {
        var result = await _mediator.Send(new ResetPasswordCommand(request.Email, request.Token, request.NewPassword));
        return result.ToHttpResponse(this);
    }

    private string? GetClientOrigin()
    {
        // Try Origin header first (standard for CORS)
        if (Request.Headers.TryGetValue("Origin", out var origin))
            return origin.ToString();

        // Try Referer header
        if (Request.Headers.TryGetValue("Referer", out var referer))
        {
            var uri = new Uri(referer.ToString());
            return $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : $":{uri.Port}")}";
        }

        // Construct from X-Forwarded-Proto and Host (for proxied requests)
        if (Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto) &&
            Request.Headers.TryGetValue("X-Forwarded-Host", out var host))
        {
            return $"{proto}://{host}";
        }

        // Fallback to request scheme and host
        if (Request.Host.HasValue)
            return $"{Request.Scheme}://{Request.Host}";

        return null;
    }
}
