using System.Security.Claims;
using Application.Interfaces;
using Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Web.Middleware;

public class JwtMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<JwtMiddleware> _logger;

    public JwtMiddleware(RequestDelegate next, ILogger<JwtMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IJwtService jwtService)
    {
        var token = ExtractBearerToken(context);

        if (token is not null)
        {
            var result = jwtService.ValidateAccessToken(token);

            if (result.IsSuccess)
            {
                var claims = result.Value!;
                var identity = new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, claims.UserId.ToString()),
                        new Claim(ClaimTypes.Email,          claims.Email),
                        new Claim(AppClaimTypes.FirstName, claims.FirstName ?? string.Empty),
                        new Claim(AppClaimTypes.LastName,  claims.LastName ?? string.Empty),
                        new Claim(AppClaimTypes.Picture,   claims.ProfilePictureUrl ?? string.Empty),
                    },
                    authenticationType: "Bearer"
                );

                context.User = new ClaimsPrincipal(identity);
            }
            else
            {
                context.Items["AuthError"] = result.Error;
                _logger.LogDebug("JWT validation failed");
            }
        }

        await _next(context);
    }

    private static string? ExtractBearerToken(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        var token = authHeader["Bearer ".Length..].Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}