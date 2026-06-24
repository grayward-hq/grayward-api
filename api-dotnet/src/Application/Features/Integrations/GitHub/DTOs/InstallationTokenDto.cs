using System.Text.Json.Serialization;

namespace Application.Features.Integrations.GitHub.DTOs;

public record InstallationTokenDto(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);