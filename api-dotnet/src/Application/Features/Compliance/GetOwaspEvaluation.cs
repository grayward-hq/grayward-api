using Application.Features.Auth.DTOs;
using Application.Features.Compliance.DTOs;
using Application.Helpers;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using FluentValidation;
using MediatR;

namespace Application.Features.Compliance;

public record GetOwaspEvaluationQuery(Guid DomainId) 
    : IRequest<Result<OwaspEvaluationDto>>;

public class GetOwaspEvaluationHandler(
    IScanRepository scanRepo,
    IDomainRepository domainRepo,
    ICurrentUser currentUser,
    OwaspEvaluationEngine engine)
    : IRequestHandler<GetOwaspEvaluationQuery, Result<OwaspEvaluationDto>>
{
    public async Task<Result<OwaspEvaluationDto>> Handle(
        GetOwaspEvaluationQuery query, CancellationToken ct)
    {
        // Ownership check — same pattern used across the codebase
        var domain = await domainRepo.FindUserDomainById(
            currentUser.UserId, query.DomainId, ct);

        if (domain is null)
            return Result<OwaspEvaluationDto>.Failure(
                Error.NotFound("Domain not found."));

        var scan = await scanRepo.FindLatestCompletedByDomain(query.DomainId, ct);

        if (scan is null)
            return Result<OwaspEvaluationDto>.Failure(
                Error.NotFound("No completed scan found for this domain."));

        // FindLatestCompletedByDomain does not load findings — fetch with findings
        var scanWithFindings = await scanRepo.FindByIdWithFindings(scan.Id, ct);

        var result = engine.Evaluate(scanWithFindings!.Findings);

        return Result<OwaspEvaluationDto>.Success(new OwaspEvaluationDto(
            ScanId: scan.Id,
            OverallScore: result.OverallScore,
            ComplianceTier: result.ComplianceTier,
            Categories: result.Categories.Select(c => new OwaspCategoryDto(
                Code: c.Code,
                Name: c.Name,
                Score: c.Score,
                ComplianceStatus: c.ComplianceStatus,
                FindingCount: c.Findings.Count
            )).ToList()
        ));
    }
}