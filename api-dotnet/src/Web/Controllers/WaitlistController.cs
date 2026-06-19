using Application.Features.Auth.DTOs;
using Application.Features.Waitlist.Commands;
using Application.Features.Waitlist.DTOs;
using Application.Features.Waitlist.Queries;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Web.Extensions;

namespace Web.Controllers;

/// <summary>
/// Waitlist endpoint for users to join Vulnwatch waitlist and admins to manage entries.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class WaitlistController : ControllerBase
{
    private readonly IMediator _mediator;

    public WaitlistController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Join the Vulnwatch waitlist.
    /// </summary>
    /// <param name="request">Email and optional company name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Successfully added to waitlist.</response>
    /// <response code="400">Validation error.</response>
    /// <response code="409">Email already on waitlist or already registered.</response>
    [HttpPost("join")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitExtensions.GeneralPolicy)]
    public async Task<ActionResult<Result<WaitlistResponse>>> JoinWaitlist(
        [FromBody] JoinWaitlistRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new JoinWaitlistCommand(request.Email, request.CompanyName), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Check waitlist status for an email address.
    /// </summary>
    /// <param name="email">Email address to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Waitlist status retrieved.</response>
    /// <response code="400">Invalid email format.</response>
    /// <response code="404">Email not found on waitlist.</response>
    [HttpGet("status")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitExtensions.GeneralPolicy)]
    public async Task<ActionResult<Result<WaitlistStatusResponse>>> GetStatus(
        [FromQuery] string email,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "Email is required" });

        var result = await _mediator.Send(
            new GetWaitlistStatusQuery(email), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Verify email confirmation token.
    /// </summary>
    /// <param name="email">Email address to verify.</param>
    /// <param name="token">Verification token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Email successfully verified.</response>
    /// <response code="400">Invalid or expired token.</response>
    /// <response code="404">Email not found.</response>
    [HttpGet("verify")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitExtensions.GeneralPolicy)]
    public async Task<ActionResult<Result<MessageResponse>>> VerifyEmail(
        [FromQuery] string email,
        [FromQuery] string token,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            return BadRequest(new { error = "Email and token are required" });

        var result = await _mediator.Send(
            new VerifyWaitlistEmailCommand(email, token), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Cancel waitlist entry.
    /// </summary>
    /// <param name="request">Email to remove from waitlist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Successfully removed from waitlist.</response>
    /// <response code="400">Invalid email.</response>
    /// <response code="404">Email not found on waitlist.</response>
    [HttpPost("cancel")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitExtensions.GeneralPolicy)]
    public async Task<ActionResult<Result<MessageResponse>>> CancelWaitlist(
        [FromBody] CancelWaitlistRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new CancelWaitlistCommand(request.Email, request.Token), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Get paginated list of waitlist entries (Admin only).
    /// </summary>
    /// <param name="status">Filter by status (optional).</param>
    /// <param name="page">Page number (default: 1).</param>
    /// <param name="pageSize">Items per page (default: 50, max: 500).</param>
    /// <param name="searchEmail">Search by email (optional).</param>
    /// <param name="sortBy">Sort field: position, email, status, createdat (default: position).</param>
    /// <param name="sortOrder">Sort order: asc, desc (default: asc).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">List of waitlist entries.</response>
    /// <response code="401">Unauthorized.</response>
    /// <response code="403">Forbidden — admin access required.</response>
    [HttpGet("admin/list")]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting(RateLimitExtensions.GeneralPolicy)]
    public async Task<ActionResult<Result<PagedResult<WaitlistListItemDto>>>> GetWaitlistList(
        [FromQuery] WaitlistStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? searchEmail = null,
        [FromQuery] string sortBy = "position",
        [FromQuery] string sortOrder = "asc",
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetWaitlistListQuery(status, page, pageSize, searchEmail, sortBy, sortOrder), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Promote waitlist entry to full user account (Admin only).
    /// </summary>
    /// <param name="waitlistId">Waitlist entry ID.</param>
    /// <param name="firstName">First name for new user (optional).</param>
    /// <param name="lastName">Last name for new user (optional).</param>
    /// <param name="sendInvitationEmail">Send invitation email (default: true).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">User successfully promoted.</response>
    /// <response code="400">Invalid request or not confirmed.</response>
    /// <response code="401">Unauthorized.</response>
    /// <response code="403">Forbidden — admin access required.</response>
    /// <response code="404">Waitlist entry not found.</response>
    /// <response code="409">Email already registered as user.</response>
    [HttpPost("admin/{waitlistId:guid}/promote")]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting(RateLimitExtensions.GeneralPolicy)]
    public async Task<ActionResult<Result<PromoteWaitlistResponse>>> PromoteWaitlist(
        [FromRoute] Guid waitlistId,
        [FromQuery] string? firstName = null,
        [FromQuery] string? lastName = null,
        [FromQuery] bool sendInvitationEmail = true,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new PromoteWaitlistCommand(waitlistId, firstName, lastName, sendInvitationEmail), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Update waitlist entry (Admin only).
    /// </summary>
    /// <param name="waitlistId">Waitlist entry ID.</param>
    /// <param name="companyName">Company name (optional).</param>
    /// <param name="notes">Admin notes (optional).</param>
    /// <param name="status">New status (optional).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Entry updated successfully.</response>
    /// <response code="400">Invalid request.</response>
    /// <response code="401">Unauthorized.</response>
    /// <response code="403">Forbidden — admin access required.</response>
    /// <response code="404">Entry not found.</response>
    [HttpPut("admin/{waitlistId:guid}")]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting(RateLimitExtensions.GeneralPolicy)]
    public async Task<ActionResult<Result<WaitlistListItemDto>>> UpdateWaitlist(
        [FromRoute] Guid waitlistId,
        [FromQuery] string? companyName = null,
        [FromQuery] string? notes = null,
        [FromQuery] WaitlistStatus? status = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdateWaitlistCommand(waitlistId, companyName, notes, status), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Delete waitlist entry (Admin only).
    /// </summary>
    /// <param name="waitlistId">Waitlist entry ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">Entry deleted successfully.</response>
    /// <response code="401">Unauthorized.</response>
    /// <response code="403">Forbidden — admin access required.</response>
    /// <response code="404">Entry not found.</response>
    [HttpDelete("admin/{waitlistId:guid}")]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting(RateLimitExtensions.GeneralPolicy)]
    public async Task<ActionResult<Result<MessageResponse>>> DeleteWaitlist(
        [FromRoute] Guid waitlistId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new DeleteWaitlistCommand(waitlistId), ct);
        
        if (!result.IsSuccess)
            return result.ToHttpResponse(this);

        return NoContent();
    }

    /// <summary>
    /// Get waitlist analytics (Admin only).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Analytics data.</response>
    /// <response code="401">Unauthorized.</response>
    /// <response code="403">Forbidden — admin access required.</response>
    [HttpGet("admin/analytics")]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting(RateLimitExtensions.GeneralPolicy)]
    public async Task<ActionResult<Result<WaitlistAnalyticsDto>>> GetAnalytics(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetWaitlistAnalyticsQuery(), ct);
        return result.ToHttpResponse(this);
    }
}
