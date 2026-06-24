namespace Application.Features.Integrations.GitHub.DTOs;

public record ConnectGitHubRequest(long InstallationId, string SetupAction);