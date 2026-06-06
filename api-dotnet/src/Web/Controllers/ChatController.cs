using System.Text.Json;
using Application.Features.Chat;
using Application.Features.Chat.DTOs;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Web.Extensions;

namespace Web.Controllers;

[EnableRateLimiting(RateLimitExtensions.GeneralPolicy)]
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ChatController(IMediator mediator, ILogger<ChatController> logger) : ControllerBase
{
    /// <summary>Start a chat session for a completed scan report.</summary>
    [HttpPost("scans/{scanId:guid}/session")]
    public async Task<ActionResult<Result<ChatMessageResponse>>> StartSession(
        Guid scanId, CancellationToken ct)
    {
        var result = await mediator.Send(new StartScanChatCommand(scanId), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>Send a message in an active chat session.</summary>
    [HttpPost("sessions/{sessionId:guid}/messages")]
    public async Task<ActionResult<Result<ChatMessageResponse>>> SendMessage(
        Guid sessionId,
        [FromBody] ChatMessageRequest request,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new SendChatMessageCommand(sessionId, request.Message), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Streams a chat response as Server-Sent Events.
    /// Clients should connect with Accept: text/event-stream.
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/stream")]
    public async Task StreamMessage(
        Guid sessionId,
        [FromBody] ChatMessageRequest request,
        CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        // Required for nginx/proxies — prevents buffering
        Response.Headers["X-Accel-Buffering"] = "no";

        await Response.Body.FlushAsync(ct);

        try
        {
            await foreach (var chunk in mediator.CreateStream(
                new StreamChatMessageCommand(sessionId, request.Message), ct))
            {
                // SSE format: "data: <payload>\n\n"
                var line = $"data: {JsonSerializer.Serialize(new { text = chunk })}\n\n";
                await Response.WriteAsync(line, ct);
                await Response.Body.FlushAsync(ct);
            }

            // Signal completion to the client
            await Response.WriteAsync("data: [DONE]\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal, not an error
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Streaming error for session {SessionId}", sessionId);
            var error = $"data: {JsonSerializer.Serialize(new { error = "Stream interrupted." })}\n\n";
            await Response.WriteAsync(error, ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    /// <summary>End and clean up a chat session.</summary>
    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<ActionResult<Result<Unit>>> EndSession(
        Guid sessionId,
        CancellationToken ct)
    {
        var result = await mediator.Send(new EndChatSessionCommand(sessionId), ct);
        return result.ToHttpResponse(this);

    }
}