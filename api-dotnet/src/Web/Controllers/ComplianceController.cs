using Application.Features.Auth.DTOs;
using Application.Features.BreachMonitoring;
using Application.Features.BreachMonitoring.DTOs;
using Application.Features.Compliance;
using Application.Features.Compliance.DTOs;
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
public class ComplianceController(IMediator mediator) : ControllerBase
{

    [HttpGet("domains/{domainId:guid}/owasp")]
    public async Task<ActionResult<Result<OwaspEvaluationDto>>> GetOwaspEvaluation(
    Guid domainId,
    CancellationToken ct)
    {
        var result = await mediator.Send(new GetOwaspEvaluationQuery(domainId), ct);
        return result.ToHttpResponse(this);
    }

    [HttpGet("domains/{domainId:guid}/report/pdf")]
    public async Task<IActionResult> DownloadReport(Guid domainId, CancellationToken ct)
    {
        var result = await mediator.Send(new GenerateReportCommand(domainId), ct);

        if (!result.IsSuccess)
            return result.Error!.Code switch
            {
                ErrorCode.NotFound   => NotFound(result),
                ErrorCode.Validation => BadRequest(result),
                ErrorCode.Forbidden  => Forbid(),
                _                    => StatusCode(500, result)
            };

        return File(result.Value!, "application/pdf",
            $"vulnwatch-report-{domainId:N}.pdf");
    }

    [HttpGet("monitored-emails/{domainId:guid}")]
    public async Task<ActionResult<Result<List<MonitoredEmailDto>>>> GetAll(
        Guid domainId, CancellationToken ct)
        => (await mediator.Send(new GetMonitoredEmailsQuery(domainId), ct))
            .ToHttpResponse(this);

    [HttpPost("monitored-emails")]
    public async Task<ActionResult<Result<MonitoredEmailDto>>> Add(
        Guid domainId, [FromBody] AddMonitoredEmailRequest request, CancellationToken ct)
        => (await mediator.Send(new AddMonitoredEmailCommand(domainId, request.Email), ct))
            .ToHttpResponse(this);

    [HttpDelete("monitored-emails/{emailId:guid}")]
    public async Task<ActionResult<Result<MessageResponse>>> Remove(
        Guid domainId, Guid emailId, CancellationToken ct)
        => (await mediator.Send(new RemoveMonitoredEmailCommand(domainId, emailId), ct))
            .ToHttpResponse(this);
}

