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

public record AddMonitoredEmailCommand(Guid DomainId, string Email)
    : IRequest<Result<MonitoredEmailDto>>;

public class AddMonitoredEmailValidator : AbstractValidator<AddMonitoredEmailCommand>
{
    public AddMonitoredEmailValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(254);
    }
}

public class AddMonitoredEmailHandler(
    IDomainRepository domainRepo,
    IMonitoredEmailRepository emailRepo,
    ICurrentUser currentUser)
    : IRequestHandler<AddMonitoredEmailCommand, Result<MonitoredEmailDto>>
{
    private const int MaxEmailsPerDomain = 5;

    public async Task<Result<MonitoredEmailDto>> Handle(
        AddMonitoredEmailCommand cmd, CancellationToken ct)
    {
        var domain = await domainRepo.FindUserDomainById(
            currentUser.UserId, cmd.DomainId, ct);

        if (domain is null)
            return Result<MonitoredEmailDto>.Failure(Error.NotFound("Domain not found."));

        var count = await emailRepo.CountByDomain(cmd.DomainId, ct);
        if (count >= MaxEmailsPerDomain)
            return Result<MonitoredEmailDto>.Failure(
                Error.Validation($"Maximum of {MaxEmailsPerDomain} monitored emails per domain."));

        var existing = await emailRepo.FindByDomainAndEmail(
            cmd.DomainId, cmd.Email.ToLowerInvariant(), ct);

        if (existing is not null)
            return Result<MonitoredEmailDto>.Failure(
                Error.Validation("This email is already being monitored for this domain."));

        var email = MonitoredEmail.Create(
            currentUser.UserId, cmd.DomainId, cmd.Email.ToLowerInvariant());

        await emailRepo.AddAsync(email, ct);
        await emailRepo.SaveChangesAsync(ct);

        return Result<MonitoredEmailDto>.Success(MonitoredEmailDto.From(email));
    }
}