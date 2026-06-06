using Application.Features.Auth.DTOs;
using Application.Features.Integrations.Slack;
using Application.Features.Integrations.Slack.DTOs;
using Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Web.Extensions;

namespace Web.Controllers;

[EnableRateLimiting(RateLimitExtensions.GeneralPolicy)]
[AllowAnonymous]               
[ApiController]
[Route("api/[controller]")]
public class IntegrationsController(IMediator mediator, IConfiguration config)
    : ControllerBase
{
    
    /// <summary>
    /// Retrieves the current Slack integration status for the authenticated user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns Slack connection status.</response>
    /// <response code="401">User is not authenticated.</response>
    [Authorize]
    [HttpGet("slack/status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var result = await mediator.Send(new GetSlackStatusQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(500, result);
    }

    /// <summary>
    /// Initiates the Slack OAuth flow. Must be called by an authenticated user.
    /// Redirects the browser to Slack's authorization page.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns the Slack authorization URL.</response>
    /// <response code="401">User is not authenticated.</response>
    [Authorize]           
    [HttpGet("slack/authorize")]
    public async Task<ActionResult<Result<SlackAuthUrlResponse>>> Connect(CancellationToken ct)
    {
        var result = await mediator.Send(new ConnectSlackCommand(), ct);

        if (!result.IsSuccess)
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                result);

        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Slack redirects here after the user approves or denies the OAuth request.
    /// No JWT required — the state parameter is the only auth mechanism here.
    /// </summary>
    [HttpGet("slack/callback")]
    public async Task<ActionResult<Result<SlackCallbackResponse>>> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        var frontendBase = config["FrontendUrl:SlackCallback"]
                        ?? config["FrontendUrl:Verify"]!
                            .Replace("/verify", "");

        if (!string.IsNullOrWhiteSpace(error))
            return Redirect(
                $"{frontendBase}?slack=denied&reason={Uri.EscapeDataString(error)}");

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return Redirect(
                $"{frontendBase}?slack=error&reason=missing_params");

        var result = await mediator.Send(
            new HandleSlackCallbackCommand(code, state), ct);

        if (!result.IsSuccess)
            return Redirect(
                $"{frontendBase}?slack=error" +
                $"&reason={Uri.EscapeDataString(result.Error!.Message)}");

        return Redirect(
            $"{frontendBase}?slack=success" +
            $"&team={Uri.EscapeDataString(result.Value!.TeamName)}");
    }

    /// <summary>
    /// Disconnects the currently connected Slack workspace from the user account.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Slack successfully disconnected.</response>
    /// <response code="400">No active Slack connection found.</response>
    /// <response code="401">User is not authenticated.</response>
    [Authorize]
    [HttpDelete("slack")]
    public async Task<ActionResult<Result<MessageResponse>>> DisconnectSlack(
        CancellationToken ct)
    {
        var result = await mediator.Send(new DisconnectSlackCommand(), ct);
        return result.ToHttpResponse(this);
    }
}