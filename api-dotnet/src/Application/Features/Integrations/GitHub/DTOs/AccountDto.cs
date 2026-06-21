
using System.Text.Json.Serialization;

namespace Application.Features.Integrations.GitHub.DTOs;

public record AccountDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("type")] string Type);