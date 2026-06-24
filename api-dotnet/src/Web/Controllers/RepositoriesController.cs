using Application.Features.Auth.DTOs;
using Application.Features.Integrations.GitHub;
using Application.Features.Integrations.GitHub.DTOs;
using Application.Features.Integrations.Slack;
using Application.Features.Integrations.Slack.DTOs;
using Application.Features.Repository;
using Application.Features.Repository.DTOs;
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
public class RepositoriesController(IMediator mediator)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<Result<PagedResult<RepoSummary>>>> GetUserDomains([FromQuery] GetReposRequest request, CancellationToken ct)
    {

        var query = new GetRepositoriesQuery(request.Search, request.Status,
                                        request.SortBy, request.Order, request.Page, request.PageSize);

        var result = await mediator.Send(query, ct);
        return result.ToHttpResponse(this);

    }

    [HttpGet("{repositoryId:guid}")]
    public async Task<ActionResult<Result<RepoDetailDto>>> GetDetail(
        Guid repositoryId, [FromQuery] int trendDays = 30, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetRepoDetailQuery(repositoryId, trendDays), ct);
        return result.ToHttpResponse(this);
    }

    [HttpPut("{repositoryId:guid}/settings")]
    public async Task<ActionResult<Result<MessageResponse>>> UpdateSettings(
        Guid repositoryId, [FromBody] UpdateRepoSettingsRequest body, CancellationToken ct)
    {
        var cmd = new UpdateRepoSettingsCommand(
            repositoryId,
            body.PeriodicScanEnabled, body.PeriodicScanFrequency,
            body.EventScanEnabled, body.Triggers,
            body.AlertChannels, body.Version);

        var result = await mediator.Send(cmd, ct);
        return result.ToHttpResponse(this);
    }
}