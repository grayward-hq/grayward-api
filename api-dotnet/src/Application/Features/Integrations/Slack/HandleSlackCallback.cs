using Application.Features.Integrations.Slack.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using Domain.Meta;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace Application.Features.Integrations.Slack;

public record HandleSlackCallbackCommand(
    string Code,
    string State) : IRequest<Result<SlackCallbackResponse>>;

public class HandleSlackCallbackHandler(
    IRedisService stateStore,
    ISlackService slack,
    IIntegrationRepository integrations,
    UserManager<User> userManager,
    ITokenService tokenProtector)
    : IRequestHandler<HandleSlackCallbackCommand, Result<SlackCallbackResponse>>
{
    public async Task<Result<SlackCallbackResponse>> Handle(
        HandleSlackCallbackCommand cmd, CancellationToken ct)
    {
        var userId = await stateStore.ValidateSlackState(cmd.State, ct);
        if (userId is null || userId == Guid.Empty)
            return Result<SlackCallbackResponse>.Failure(
                Error.Validation("Invalid or expired state parameter."));

        var user = await userManager.FindByIdAsync(userId.Value.ToString());
        if (user is null)
            return Result<SlackCallbackResponse>.Failure(
                Error.Validation("User not found. Please try logging in again."));

        var tokenResult = await slack.ExchangeCode(cmd.Code, ct);

        if(!tokenResult.IsSuccess)
        {
            return Result<SlackCallbackResponse>.Failure(
                Error.Internal($"Slack token exchange failed: {tokenResult.Error?.Message ?? "unknown error"}"));
        }

        if (tokenResult.Value is not { } token)
        {
            return Result<SlackCallbackResponse>.Failure(
                Error.Internal("Slack token response was null."));
        }

        if (!token.Ok ||
            string.IsNullOrWhiteSpace(token.AccessToken) ||
            token.Team is null ||
            string.IsNullOrWhiteSpace(token.Team.Id) ||
            string.IsNullOrWhiteSpace(token.Team.Name))
        {
            return Result<SlackCallbackResponse>.Failure(
                Error.Validation($"Slack token exchange failed: {token.Error ?? "unknown error"}"));
        }

        var existing = await integrations.GetByUserAndProvider(
            userId.Value, IntegrationProvider.Slack, ct);

        var metadata = new Dictionary<string, string>
        {
            [SlackMetadataKeys.BotAccessToken] = tokenProtector.Protect(token.AccessToken ?? ""),
            [SlackMetadataKeys.BotUserId] = token.BotUserId ?? "",
            [SlackMetadataKeys.AppId] = token.AppId ?? "",
            [SlackMetadataKeys.Scope] = token.Scope ?? "",
            [SlackMetadataKeys.TeamName] = token.Team.Name ?? "",
            [SlackMetadataKeys.WebhookUrl] = token.IncomingWebhook?.Url ?? "",
            [SlackMetadataKeys.WebhookChannel] = token.IncomingWebhook?.Channel ?? "",
        };

        if (existing is not null)
        {
            existing.Activate();
            // InstallationId holds the team ID for Slack, so update it in case the user connects to a different workspace
            existing.UpdateInstallation(token.Team.Id!);
            existing.UpsertMetadata(metadata);
        }
        else
        {
            var integration = Integration.Create(
                userId.Value,
                provider: IntegrationProvider.Slack,
                installationId: token.Team.Id!,
                metadata: metadata);

            integration.Activate();
            await integrations.AddAsync(integration, ct);
        }

        await integrations.SaveChangesAsync(ct);

        return Result<SlackCallbackResponse>.Success(
            new SlackCallbackResponse(
                token.Team.Name ?? "Your workspace",
                token.Team.Id!));
    }
}