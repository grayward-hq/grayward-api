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
    [HttpGet("sessions")]
    public async Task<ActionResult<Result<IReadOnlyList<SessionDto>>>> GetSessions(
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new GetSessionsQuery(), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>Revokes a specific session by id.</summary>
    [Authorize]
    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<ActionResult<Result<MessageResponse>>> RevokeSession(
        Guid sessionId, CancellationToken ct)
    {
        var result = await _mediator.Send(new RevokeSessionCommand(sessionId), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>Revokes all sessions except the current one.</summary>
    [Authorize]
    [HttpDelete("sessions")]
    public async Task<ActionResult<Result<MessageResponse>>> RevokeAllOtherSessions(
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new RevokeAllOtherSessionsCommand(), ct);
        return result.ToHttpResponse(this);
    }

    private (string? ip, string? ua) GetRequestContext()
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString()
              ?? Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var ua = Request.Headers.UserAgent.ToString();
        return (ip, ua);
    }

    private string? GetClientOrigin()
    {
        // Try Origin header first (standard for CORS)
        if (Request.Headers.TryGetValue("Origin", out var origin))
            return origin.ToString();

        // Try Referer header
        if (Request.Headers.TryGetValue("Referer", out var referer))
        {
            var uri = new Uri(referer.ToString());
            return $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : $":{uri.Port}")}";
        }

        // Construct from X-Forwarded-Proto and Host (for proxied requests)
        if (Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto) &&
            Request.Headers.TryGetValue("X-Forwarded-Host", out var host))
        {
            return $"{proto}://{host}";
        }

        // Fallback to request scheme and host
        if (Request.Host.HasValue)
            return $"{Request.Scheme}://{Request.Host}";

        return null;
    }
}
