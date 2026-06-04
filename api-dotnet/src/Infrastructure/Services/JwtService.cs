using System.Security.Claims;
using System.Text;
using Application.Helpers;
using Application.Interfaces;
using Domain.Common;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Services;

public class JwtService : IJwtService
{
    private readonly IConfiguration _config;

    public JwtService(IConfiguration config)
    {
        _config = config;
    }

    public string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:SecretKey"]!));
        
        var expireMinutesRaw = _config["Jwt:ExpireInMinute"];
        var expireMinutes = int.TryParse(expireMinutesRaw, out var minutes) && minutes > 0 ? minutes : 60;

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email,          user.Email!),
            new Claim(AppClaimTypes.FirstName, user.FirstName ?? string.Empty),
            new Claim(AppClaimTypes.LastName,  user.LastName ?? string.Empty),
            new Claim(AppClaimTypes.Picture,   user.ProfilePictureUrl ?? string.Empty),
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expireMinutes),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    public Result<TokenClaims> ValidateAccessToken(string token)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:SecretKey"]!));
        var issuer = _config["Jwt:Issuer"]!;
        var audience = _config["Jwt:Audience"]!;
        var handler = new JwtSecurityTokenHandler();

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        try
        {
            var principal = handler.ValidateToken(token, validationParams, out _);
            var userId    = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var email     = principal.FindFirstValue(ClaimTypes.Email);
            var firstName = principal.FindFirstValue(AppClaimTypes.FirstName);
            var lastName  = principal.FindFirstValue(AppClaimTypes.LastName);
            var picture   = principal.FindFirstValue(AppClaimTypes.Picture);
            var role = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

            if (userId is null || email is null || !Guid.TryParse(userId, out var parsedUserId))
                return Result<TokenClaims>.Failure(Error.Unauthorized("Token is invalid"));

            return Result<TokenClaims>.Success(new TokenClaims(
                parsedUserId, email, firstName, lastName, picture));
        }
        catch (SecurityTokenMalformedException)
        {
            return Result<TokenClaims>.Failure(Error.Unauthorized("Token is malformed."));
        }
        catch (SecurityTokenExpiredException)
        {
            return Result<TokenClaims>.Failure(Error.Unauthorized("Token has expired."));
        }
        catch (SecurityTokenException)
        {
            return Result<TokenClaims>.Failure(Error.Unauthorized("Token is invalid."));
        }
    }
}

public static class AppClaimTypes
{
    public const string UserId = "userId";
    public const string Email = "email";
    public const string FirstName = "firstName";
    public const string LastName = "lastName";
    public const string Picture = "picture";
}



