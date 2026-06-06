using Application.Features.Domain.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;

namespace Application.Features.Domain;

public record ResendDomainTokenCommand(Guid DomainId) : IRequest<Result<RegisterDomainResponse>>;


public class ResendDomainTokenHandler(
    IDomainRepository domains,
    ICurrentUser currentUser,
    ITokenService tokenService)
    : IRequestHandler<ResendDomainTokenCommand, Result<RegisterDomainResponse>>
{

    public async Task<Result<RegisterDomainResponse>> Handle(ResendDomainTokenCommand cmd, CancellationToken ct)
    {
        var domain = await domains.FindUserDomainById(currentUser.UserId, cmd.DomainId, ct);

        if (domain is null)
            return Result<RegisterDomainResponse>.Failure(Error.NotFound("Domain not registered."));

        if (domain.VerificationStatus == VerificationStatus.Verified)
            return Result<RegisterDomainResponse>.Failure(Error.Validation("Domain is already verified"));

        var (rawToken, tokenHash) = tokenService.Generate();

        domain.RegenerateToken(tokenHash);

        await domains.SaveChangesAsync(ct);

        return Result<RegisterDomainResponse>.Success(RegisterDomainResponse.Create(rawToken, domain));
    }
}