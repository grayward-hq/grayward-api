namespace Application.Features.Auth.DTOs;

public record RefreshTokenRequest(string RefreshToken)
{
    public static RefreshTokenRequest Create(string refreshToken) => new(refreshToken);
}