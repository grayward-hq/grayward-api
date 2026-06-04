using Application.Features.Auth;
using Application.Features.Auth.DTOs;
using Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Web.Extensions;

namespace Web.Controllers;

[EnableRateLimiting(RateLimitExtensions.AuthPolicy)]
[ApiController]
[Route("api/[controller]")]
public class SessionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SessionsController(IMediator mediator) => _mediator = mediator;


    /// <summary>Lists all active sessions for the current user.</summary>
    [Authorize]
    [HttpGet]
    public async Task<ActionResult<Result<IReadOnlyList<SessionDto>>>> GetSessions(
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new GetSessionsQuery(), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>Revokes a specific session by id.</summary>
    [Authorize]
    [HttpDelete("{sessionId:guid}")]
    public async Task<ActionResult<Result<MessageResponse>>> RevokeSession(
        Guid sessionId, CancellationToken ct)
    {
        var result = await _mediator.Send(new RevokeSessionCommand(sessionId), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>Revokes all sessions except the current one.</summary>
    [Authorize]
    [HttpDelete]
    public async Task<ActionResult<Result<MessageResponse>>> RevokeAllOtherSessions(
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new RevokeAllOtherSessionsCommand(), ct);
        return result.ToHttpResponse(this);
    }
}
