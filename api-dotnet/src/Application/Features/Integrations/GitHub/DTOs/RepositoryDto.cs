using System.Text.Json.Serialization;

namespace Application.Features.Integrations.GitHub.DTOs;

public record RepositoryDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("private")] bool Private,
    [property: JsonPropertyName("default_branch")] string DefaultBranch,
    [property: JsonPropertyName("html_url")] string HtmlUrl);