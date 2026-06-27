using System.Text.Json.Serialization;

namespace Application.Features.Integrations.GitHub.DTOs;

public record InstallationRepositoriesDto(
    [property: JsonPropertyName("total_count")] int TotalCount,
    [property: JsonPropertyName("repositories")] List<RepositoryDto> Repositories);