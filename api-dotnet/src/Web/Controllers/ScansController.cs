using Application.Features.Scans;
using Application.Features.Scans.DTOs;
using Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Web.Extensions;
using Microsoft.AspNetCore.RateLimiting;



namespace Web.Controllers;

/**
 * ScansController: Handles all HTTP requests related to vulnerability scans.
 * Intern-friendly: This is the entry point for the API.
 */
[EnableRateLimiting(RateLimitExtensions.GeneralPolicy)]
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ScansController : ControllerBase
{
    private readonly IMediator _mediator;

    public ScansController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Initiates a new vulnerability scan for a domain.
    /// </summary>
    /// <param name="idempotencyKey">
    /// Unique key used to prevent duplicate scan submissions.
    /// </param>
    /// <param name="body">Scan configuration payload.</param>
    /// <response code="200">Scan successfully started.</response>
    /// <response code="400">Invalid scan request or duplicate idempotency key.</response>
    /// <response code="401">User is not authenticated.</response>
    [HttpPost]
    public async Task<ActionResult<Result<StartScanResponse>>> InitiateScan(
        [FromHeader(Name = "Idempotency-Key")] Guid idempotencyKey,
        [FromBody] StartScanRequest body)
    {
        var command = new StartScanCommand(body.Domain, body.Coverage, body.SurfaceTypes, idempotencyKey);
        var result = await _mediator.Send(command);
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Retrieves the scan history for a specific domain.
    /// </summary>
    /// <param name="domainId">Domain identifier (GUID).</param>
    /// <param name="request">Scan history query parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns the scan history.</response>
    /// <response code="401">User is not authenticated.</response>
    /// <response code="404">Domain not found.</response>
    [HttpGet("{domainId:guid}/history")]
    public async Task<ActionResult<Result<PagedResult<ScanSummary>>>> GetScanHistory(Guid domainId, [FromQuery] GetScanHistoryRequest request, CancellationToken ct)
    {
        var query = new GetScanHistoryQuery(domainId, request.Status, request.Coverage,
                                        request.SortBy, request.Order, request.Page, request.PageSize);

        var result = await _mediator.Send(query, ct);
        return result.ToHttpResponse(this);

    }

    /// <summary> 
    /// Retrieves the detailed report for a specific scan.
    /// </summary>
    /// <param name="scanId">Scan identifier (GUID).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns the scan report.</response>
    /// <response code="401">User is not authenticated.</response>
    /// <response code="404">Scan not found.</response>
    [HttpGet("{scanId:guid}/report")]
    public async Task<ActionResult<Result<ScanReportDto>>> GetReport(Guid scanId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetScanReportQuery(scanId), ct);
        return result.ToHttpResponse(this);
    }
}
