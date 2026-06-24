using System.Text.Json;
using Application.Features.Auth.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using Domain.Meta;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Features.Integrations.GitHub;

public record ConnectGitHubCommand(long InstallationId, string SetupAction)
    : IRequest<Result<MessageResponse>>;

public class ConnectGitHubHandler(
    IGitHubAppClient github, 
    IVulnWatchDbContext db,  
    IIntegrationRepository integrations,
    IMonitoredRepoRepository repos,
    ICurrentUser currentUser,
    ILogger<ConnectGitHubHandler> logger)
    : IRequestHandler<ConnectGitHubCommand, Result<MessageResponse>>
{
    public async Task<Result<MessageResponse>> Handle(ConnectGitHubCommand cmd, CancellationToken ct)
    {
        var userId = currentUser.UserId;

        var installation = await github.GetInstallation(cmd.InstallationId, ct);

        if (installation is null)
            return Result<MessageResponse>.Failure(
                Error.NotFound("GitHub installation not found or not accessible by this app."));

        var token = await github.CreateInstallationToken(installation.Id, ct);
        var installedRepos = await github.GetInstallationRepositories(token.Token, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var integration = await integrations.GetByUserAndProvider(
                userId, IntegrationProvider.GitHub, ct);

            if (integration is null)
            {
                integration = Integration.Create(userId, IntegrationProvider.GitHub,
                    installationId: cmd.InstallationId.ToString());
                integration.Activate();
                await integrations.AddAsync(integration, ct);
            } 
            else if (integration.UserId != userId)
            {
                return Result<MessageResponse>.Failure(
                    Error.Forbidden("This installation is linked to another account."));
            }
            else
            {
                integration.Activate();
                integration.UpdateInstallation(cmd.InstallationId.ToString());
            }

            await integrations.SaveChangesAsync(ct);   // flush, NOT commit (inside tx)

            // inside the transaction, replacing the foreach
            var existing = await repos.GetByUserId(userId, ct);          // List<MonitoredRepository>
            var existingByRepoId = existing.ToDictionary(r => r.RepoId);
            var incomingIds = installedRepos.Select(r => r.Id).ToHashSet();

            foreach (var r in installedRepos)
            {
                if (existingByRepoId.TryGetValue(r.Id, out var current))
                {
                    current.Activate();
                    current.UpdateFromGitHub(r.FullName, cmd.InstallationId.ToString(), r.DefaultBranch, r.Private, r.HtmlUrl);
                }
                else
                {
                    var newRepo = MonitoredRepository.Create(
                        r.Id, userId, r.FullName, r.HtmlUrl, cmd.InstallationId.ToString(), r.Private, r.DefaultBranch);
                    newRepo.Activate();
                    await repos.AddAsync(newRepo, ct);
                }
            }

            foreach (var stale in existing.Where(r => !incomingIds.Contains(r.RepoId)))
                stale.Suspend();   // or repos.Remove(stale) if you want hard deletes

            await repos.SaveChangesAsync(ct);        // flush, NOT commit

            await tx.CommitAsync(ct);                   // single commit point
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)  
        {  
            throw;  
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error connecting GitHub.");
            await tx.RollbackAsync(ct);
            return Result<MessageResponse>.Failure(
                Error.Internal("Failed to connect GitHub. No changes were saved."));
        }

        return Result<MessageResponse>.Success(
            MessageResponse.Create("GitHub connected and repositories synced."));
    }
}