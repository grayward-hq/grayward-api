using Application.Features.Auth;
using Application.Features.Auth.DTOs;
using Application.Features.Domain;
using Application.Features.Domain.DTOs;
using Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Web.Extensions;

namespace Web.Controllers;
[EnableRateLimiting(RateLimitExtensions.GeneralPolicy)]

[ApiController]
[Route("api/[controller]")]
public class DomainsController : ControllerBase
{
    private readonly IMediator _mediator;

    public DomainsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Registers a new domain for the authenticated user.
    /// </summary>
    /// <param name="request">Domain registration payload.</param>
    /// <response code="200">Domain successfully registered.</response>
    /// <response code="400">Invalid domain or validation error.</response>
    /// <response code="401">User is not authenticated.</response>
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<Result<RegisterDomainResponse>>> Register(RegisterDomainRequest request)
    {
        var result = await _mediator.Send(new RegisterDomainCommand(request.Domain));
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Resends the verification token for a previously registered domain.
    /// </summary>
    /// <param name="request">Domain for which to resend verification token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Verification token resent successfully.</response>
    /// <response code="400">Invalid domain or request.</response>
    /// <response code="401">User is not authenticated.</response>
    [Authorize]
    [HttpPost("resend-token")]
    public async Task<ActionResult<Result<RegisterDomainResponse>>> ResendToken(
        RegisterDomainRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new ResendDomainTokenCommand(request.Domain), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Verifies ownership of a domain using its unique identifier.
    /// </summary>
    /// <param name="id">Domain identifier (GUID).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Domain successfully verified.</response>
    /// <response code="400">Verification failed or domain not found.</response>
    /// <response code="401">User is not authenticated.</response>
    [Authorize]
    [HttpPut("{id:guid}/Verify")]
    public async Task<ActionResult<Result<VerifyDomainResponse>>> Verify(
        Guid id,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new VerifyDomainCommand(id), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Retrieves a paginated list of domains owned by the authenticated user.
    /// </summary>
    /// <param name="request">Pagination and filtering options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns paginated list of domains.</response>
    /// <response code="401">User is not authenticated.</response>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<Result<PagedResult<DomainSummary>>>> GetUserDomains([FromQuery] GetDomainsRequest request, CancellationToken ct)
    {

        var query = new GetDomainsQuery(request.Search, request.Status,
                                        request.SortBy, request.Order, request.Page, request.PageSize);

        var result = await _mediator.Send(query, ct);
        return result.ToHttpResponse(this);

    }

    /// <summary>
    /// Retrieves details for a specific domain by its identifier.
    /// </summary>
    /// <param name="domainId">The unique identifier of the domain.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Returns domain details.</response>
    /// <response code="401">User is not authenticated.</response>
    [HttpGet("{domainId:guid}")]
    [Authorize]
    public async Task<ActionResult<Result<DomainSummary>>> GetSingleDomain(Guid domainId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetDomainByIdQuery(domainId), ct);
        return result.ToHttpResponse(this);
    }

    /// <summary>
    /// Deletes a domain owned by the authenticated user.
    /// </summary>
    /// <param name="domainId">The unique identifier of the domain to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Domain deleted successfully.</response>
    /// <response code="401">User is not authenticated.</response>
    [HttpDelete("{domainId:guid}")]
    [Authorize]
    public async Task<ActionResult<Result<MessageResponse>>> DeleteDomain(Guid domainId, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteDomainCommand(domainId), ct);
        return result.ToHttpResponse(this);
    }

}