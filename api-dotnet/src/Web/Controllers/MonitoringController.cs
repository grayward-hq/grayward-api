using Application.Features.Monitoring;
using Application.Features.Monitoring.DTOs;
using Domain.Common;
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
public class MonitoringController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Retrieves monitoring overview for a specific domain.
    /// Includes uptime, SSL status, and recent scan results.
    /// </summary>
    /// <param name="domainId">Domain identifier (GUID).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns monitoring overview data.</response>
    /// <response code="401">User is not authenticated.</response>
    /// <response code="404">Domain not found.</response>
    [HttpGet("domains/{domainId:guid}")]
    public async Task<ActionResult<Result<MonitoringOverviewDto>>> GetOverview(
        Guid domainId,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new GetMonitoringOverviewQuery(domainId), ct);

        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Retrieves monitoring settings for a specific domain.
    /// </summary>
    /// <param name="domainId">Domain identifier (GUID).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns monitoring settings.</response>
    /// <response code="401">User is not authenticated.</response>
    /// <response code="404">Domain not found.</response>
    [HttpGet("domains/{domainId:guid}/settings")]
    public async Task<ActionResult<Result<MonitoringSettingsDto>>> GetSettings(
        Guid domainId,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new GetDomainSettingsQuery(domainId), ct);

        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Updates monitoring settings for a specific domain.
    /// </summary>
    /// <param name="domainId">Domain identifier (GUID).</param>
    /// <param name="request">The updated monitoring settings.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns the updated monitoring settings.</response>
    /// <response code="401">User is not authenticated.</response>
    /// <response code="404">Domain not found.</response>
    [HttpPut("domains/{domainId:guid}/settings")]
    public async Task<ActionResult<Result<MonitoringSettingsDto>>> UpsertSettings(
    Guid domainId,
    [FromBody] UpdateMonitoringSettingsRequest request,
    CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateMonitoringSettingsCommand(
            domainId,
            request.MonitoringEnabled,
            request.ScanFrequency,
            request.SslAlertThresholds,
            request.NotificationChannels), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Toggles monitoring on or off for a specific domain.
    /// </summary>
    /// <param name="domainId">Domain identifier (GUID).</param>
    /// <param name="enable">Indicates whether to enable or disable monitoring.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns the updated monitoring settings.</response>
    /// <response code="401">User is not authenticated.</response>
    /// <response code="404">Domain not found.</response>
    [HttpPatch("domains/{domainId:guid}/settings/toggle")]
    public async Task<ActionResult<Result<MonitoringSettingsDto>>> Toggle(
        Guid domainId,
        [FromQuery] bool enable,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new ToggleMonitoringCommand(domainId, enable), ct);
        return result.ToHttpResponse(this);
    }
}