using Application.Features.Auth.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;

namespace Application.Features.Chat;

public record EndChatSessionCommand(Guid SessionId) : IRequest<Result<Unit>>;

public class EndChatSessionHandler(
    IRedisService sessionStore,
    IScanRepository scanRepo,
    ICurrentUser currentUser)
    : IRequestHandler<EndChatSessionCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        EndChatSessionCommand cmd, CancellationToken ct)
    {
        var session = await sessionStore.GetChatSession(cmd.SessionId, ct);
        if (session is null)
            return Result<Unit>.Failure(
                Error.NotFound("Chat session not found or expired."));

        var scan = await scanRepo.FindByIdWithFindings(session.ScanId, ct);
        if (scan is null || scan.UserId != currentUser.UserId)
            return Result<Unit>.Failure(Error.Forbidden("Access denied."));

        await sessionStore.DeleteChatSession(cmd.SessionId, ct);
        return Result<Unit>.Success(Unit.Value);
    }
}