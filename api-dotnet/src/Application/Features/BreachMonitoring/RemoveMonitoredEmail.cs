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
public record RemoveMonitoredEmailCommand(Guid DomainId, Guid EmailId)
    : IRequest<Result<MessageResponse>>;

public class RemoveMonitoredEmailHandler(
    IDomainRepository domainRepo,
    IMonitoredEmailRepository emailRepo,
    ICurrentUser currentUser)
    : IRequestHandler<RemoveMonitoredEmailCommand, Result<MessageResponse>>
{
    public async Task<Result<MessageResponse>> Handle(
        RemoveMonitoredEmailCommand cmd, CancellationToken ct)
    {
        var domain = await domainRepo.FindUserDomainById(
            currentUser.UserId, cmd.DomainId, ct);

        if (domain is null)
            return Result<MessageResponse>.Failure(Error.NotFound("Domain not found."));

        var email = await emailRepo.FindById(cmd.EmailId, ct);

        if (email is null || email.DomainId != cmd.DomainId || email.UserId != currentUser.UserId)
            return Result<MessageResponse>.Failure(Error.NotFound("Monitored email not found."));

        if (email is null)
            return Result<MessageResponse>.Failure(Error.NotFound("Monitored email not found."));

        emailRepo.Remove(email);
        await emailRepo.SaveChangesAsync(ct);

        return Result<MessageResponse>.Success(new MessageResponse("Email successfully removed."));
    }
}