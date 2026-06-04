using Application.Features.Auth.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;

namespace Application.Features.Integrations.Slack;

public record DisconnectSlackCommand : IRequest<Result<MessageResponse>>;

public class DisconnectSlackHandler(
    IIntegrationRepository integrations,
    ICurrentUser currentUser)
    : IRequestHandler<DisconnectSlackCommand, Result<MessageResponse>>
{
    public async Task<Result<MessageResponse>> Handle(
        DisconnectSlackCommand cmd, CancellationToken ct)
    {
        var integration = await integrations.GetByUserAndProvider(currentUser.UserId, IntegrationProvider.Slack, ct);

        if (integration is null)
            return Result<MessageResponse>.Failure(
                Error.NotFound("No active Slack integration found."));

        integration.Deactivate();

        await integrations.SaveChangesAsync(ct);

        return Result<MessageResponse>.Success(
            MessageResponse.Create("Slack disconnected successfully."));
    }
}