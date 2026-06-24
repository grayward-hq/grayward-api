using Domain.Common;
using Domain.Entities;
using Application.Features.Integrations.GitHub.DTOs;

namespace Application.Interfaces;
public interface IGitHubAppClient
{
    Task<InstallationDto?> GetInstallation(long installationId, CancellationToken ct);
    Task<InstallationTokenDto> CreateInstallationToken(long installationId, CancellationToken ct);
    Task<List<RepositoryDto>> GetInstallationRepositories(string installationToken, CancellationToken ct);
}