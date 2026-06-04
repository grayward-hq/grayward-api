using Application.Features.Scans;
using Application.Features.Scans.DTOs;
using Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Web.Extensions;
using Microsoft.AspNetCore.RateLimiting;
using Application.Features.Profile.DTOs;
using Application.Features.Profile;
using Application.Features.Auth.DTOs;

namespace Web.Controllers;

/**
 * ProfileController: Handles all HTTP requests related to user profiles.
 * Intern-friendly: This is the entry point for the API.
 */
[EnableRateLimiting(RateLimitExtensions.GeneralPolicy)]
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProfileController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Retrieves the authenticated user's profile information.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns user profile data.</response>
    /// <response code="401">User is not authenticated.</response>
    [HttpGet]
    public async Task<ActionResult<Result<UserProfileDto>>> GetProfile(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetProfileQuery(), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Updates the authenticated user's profile information.
    /// </summary>
    /// <param name="request">The updated profile information.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns the updated user profile data.</response>
    /// <response code="401">User is not authenticated.</response>
    [HttpPut]
    public async Task<ActionResult<Result<UserProfileDto>>> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        var command = new UpdateProfileCommand(request.FirstName, request.LastName);
        var result = await _mediator.Send(command, ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Deletes the authenticated user's account.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns a success message.</response>
    /// <response code="401">User is not authenticated.</response>
    [HttpDelete]
    public async Task<ActionResult<Result<MessageResponse>>> DeleteAccount(CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteAccountCommand(), ct);
        return result.ToHttpResponse(this);
        
    }
}