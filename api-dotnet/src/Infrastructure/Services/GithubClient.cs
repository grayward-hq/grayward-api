using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Features.Integrations.GitHub.DTOs;
using Application.Interfaces;

namespace Infrastructure.Services;

public class GitHubAppClient : IGitHubAppClient
{
    private readonly HttpClient _http;
    private readonly GitHubAppJwtFactory _jwt;

    public GitHubAppClient(HttpClient http, GitHubAppJwtFactory jwt)
    {
        _http = http;
        _jwt = jwt;
    }

    // Verifies the installation is real (returns null on 404 = forged/stale id)
    public async Task<InstallationDto?> GetInstallation(long installationId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"app/installations/{installationId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwt.CreateJwt());
        var res = await _http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<InstallationDto>(cancellationToken: ct);
    }

    public async Task<InstallationTokenDto> CreateInstallationToken(long installationId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"app/installations/{installationId}/access_tokens");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwt.CreateJwt());
        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<InstallationTokenDto>(cancellationToken: ct))!;
    }

    public async Task<List<RepositoryDto>> GetInstallationRepositories(string installationToken, CancellationToken ct)
    {
        var repos = new List<RepositoryDto>();
        for (var page = 1; ; page++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"installation/repositories?per_page=100&page={page}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", installationToken);
            var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            var payload = await res.Content.ReadFromJsonAsync<InstallationRepositoriesDto>(cancellationToken: ct);
            if (payload?.Repositories is null or { Count: 0 }) break;
            repos.AddRange(payload.Repositories);
            if (repos.Count >= payload.TotalCount) break;
        }
        return repos;
    }
}