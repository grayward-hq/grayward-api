using Application.Features.Integrations.Slack.DTOs;
using Application.Interfaces;
using Domain.Common;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace Application.Features.Integrations.Slack;

public record ConnectSlackCommand : IRequest<Result<SlackAuthUrlResponse>>;

public record SlackAuthUrlResponse(string AuthorizationUrl);

public class ConnectSlackHandler(
    ICurrentUser currentUser,
    IRedisService stateStore,
    IConfiguration config)
    : IRequestHandler<ConnectSlackCommand, Result<SlackAuthUrlResponse>>
{
    public async Task<Result<SlackAuthUrlResponse>> Handle(
        ConnectSlackCommand request,
        CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (userId == Guid.Empty)
            return Result<SlackAuthUrlResponse>.Failure(Error.Unauthorized("Login to connect your slack account."));

        var clientId = config["Slack:ClientId"];
        var redirectUri = config["Slack:RedirectUri"];
        var scopes = config["Slack:Scopes"] ?? "incoming-webhook,chat:write";

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(redirectUri))
            return Result<SlackAuthUrlResponse>.Failure(
                Error.Internal("Slack integration is not configured."));
            
        var state = Guid.NewGuid().ToString();
        await stateStore.SaveSlackState(state, userId, ct);

        var authUrl =
            $"https://slack.com/oauth/v2/authorize?client_id={clientId}" +
            $"&scope={Uri.EscapeDataString(scopes)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&state={Uri.EscapeDataString(state)}";

        return Result<SlackAuthUrlResponse>.Success(new SlackAuthUrlResponse(authUrl));
    }
}