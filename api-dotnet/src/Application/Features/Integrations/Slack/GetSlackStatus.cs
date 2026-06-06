using Application.Features.Auth.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using Domain.Meta;
using MediatR;

namespace Application.Features.Integrations.Slack;

public record GetSlackStatusQuery : IRequest<Result<SlackStatusResponse>>;

public record SlackStatusResponse(
    bool IsConnected,
    string? TeamName,
    string? Channel,
    string? Scope,
    string? BotUserId,
    DateTimeOffset? ConnectedAt);

public class GetSlackStatusHandler(
    ICurrentUser currentUser,
    IIntegrationRepository integrations)
    : IRequestHandler<GetSlackStatusQuery, Result<SlackStatusResponse>>
{
    public async Task<Result<SlackStatusResponse>> Handle(
        GetSlackStatusQuery _, CancellationToken ct)
    {
        var integration = await integrations.GetByUserAndProvider(
            currentUser.UserId, IntegrationProvider.Slack, ct);

        if (integration is null || integration.Status == IntegrationStatus.INACTIVE)
            return Result<SlackStatusResponse>.Success(
                new SlackStatusResponse(
                    IsConnected: false,
                    TeamName: null,
                    Channel: null,
                    Scope: null,
                    BotUserId: null,
                    ConnectedAt: null));

        return Result<SlackStatusResponse>.Success(
            new SlackStatusResponse(
                IsConnected: true,
                TeamName:    integration.GetMetadata(SlackMetadataKeys.TeamName),
                Channel:     integration.GetMetadata(SlackMetadataKeys.WebhookChannel),
                Scope:       integration.GetMetadata(SlackMetadataKeys.Scope),
                BotUserId:   integration.GetMetadata(SlackMetadataKeys.BotUserId),
                ConnectedAt: integration.UpdatedAt));
    }
}

