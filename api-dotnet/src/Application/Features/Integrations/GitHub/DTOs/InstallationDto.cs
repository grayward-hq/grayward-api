
using System.Text.Json.Serialization;

namespace Application.Features.Integrations.GitHub.DTOs;

public record InstallationDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("account")] AccountDto Account,
    [property: JsonPropertyName("repository_selection")] string RepositorySelection,
    [property: JsonPropertyName("target_type")] string TargetType,
    [property: JsonPropertyName("suspended_at")] DateTimeOffset? SuspendedAt);