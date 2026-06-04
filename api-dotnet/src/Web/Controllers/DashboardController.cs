using Application.Features.Dashboard;
using Application.Features.Dashboard.DTOs;
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
public class DashboardController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Retrieves overall dashboard statistics.
    /// Typically called on every dashboard load to populate summary widgets.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns dashboard summary data.</response>
    /// <response code="401">User is not authenticated.</response>
    [HttpGet("summary")]
    public async Task<ActionResult<Result<DashboardSummaryDto>>> GetSummary(
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new GetDashboardSummaryQuery(), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Retrieves paginated list of domains for the dashboard table.
    /// Default page size is 10 to match dashboard UI layout.
    /// </summary>
    /// <param name="page">Page number (starts at 1).</param>
    /// <param name="pageSize">Number of items per page (default is 10).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns paginated domain list.</response>
    /// <response code="401">User is not authenticated.</response>
    [HttpGet("domains")]
    public async Task<ActionResult<Result<PagedResult<DashboardDomainRowDto>>>> GetDomains(
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetDashboardDomainsQuery(page, pageSize), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Retrieves recent alerts across all domains.
    /// Results are ordered from newest to oldest.
    /// </summary>
    /// <param name="limit">Maximum number of alerts to return (default is 10).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns list of recent alerts.</response>
    /// <response code="401">User is not authenticated.</response>
    [HttpGet("alerts")]
    public async Task<ActionResult<Result<IReadOnlyList<DashboardAlertDto>>>> GetAlerts(
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetDashboardAlertsQuery(limit), ct);
        return result.ToHttpResponse(this);
    }

    // /// <summary>
    // /// Priority action list for the "needs attention" panel.
    // /// Derived from domain + SSL + findings state — no extra data required.
    // /// </summary>
    // [HttpGet("attention")]
    // public async Task<ActionResult<Result<IReadOnlyList<AttentionItemDto>>>> GetAttention(
    //     CancellationToken ct = default)
    // {
    //     var result = await mediator.Send(
    //         new GetDashboardAttentionQuery(), ct);
    //     return result.ToHttpResponse(this);
    // }
}