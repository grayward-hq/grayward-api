using System.Net;
using System.Security.Cryptography;
using Application.Features.Auth.DTOs;
using Application.Features.BreachMonitoring.DTOs;
using Application.Features.Domain.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using FluentValidation;
using MediatR;

namespace Application.Features.BreachMonitoring;
public record GetMonitoredEmailsQuery(Guid DomainId)
    : IRequest<Result<List<MonitoredEmailDto>>>;

public class GetMonitoredEmailsHandler(
    IDomainRepository domainRepo,
    IMonitoredEmailRepository emailRepo,
    ICurrentUser currentUser)
    : IRequestHandler<GetMonitoredEmailsQuery, Result<List<MonitoredEmailDto>>>
{
    public async Task<Result<List<MonitoredEmailDto>>> Handle(
        GetMonitoredEmailsQuery query, CancellationToken ct)
    {
        var domain = await domainRepo.FindUserDomainById(
            currentUser.UserId, query.DomainId, ct);

        if (domain is null)
            return Result<List<MonitoredEmailDto>>.Failure(Error.NotFound("Domain not found."));

        var emails = await emailRepo.GetByDomainId(query.DomainId, ct);

        return Result<List<MonitoredEmailDto>>.Success(
            emails.Select(MonitoredEmailDto.From).ToList());
    }
}