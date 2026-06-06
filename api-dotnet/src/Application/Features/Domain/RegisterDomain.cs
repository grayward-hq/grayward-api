using System.Net;
using System.Security.Cryptography;
using Application.Features.Auth.DTOs;
using Application.Features.Domain.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Application.Features.Domain;

public record RegisterDomainCommand(string Domain) : IRequest<Result<RegisterDomainResponse>>;

public class RegisterDomainHandler : IRequestHandler<RegisterDomainCommand, Result<RegisterDomainResponse>>
{
    private readonly IDomainRepository _domains;
    private readonly ICurrentUser _currentUser;
    private readonly ITokenService _token;
    private readonly ILogger<RegisterDomainHandler> _logger;

    public RegisterDomainHandler(IDomainRepository domains, ICurrentUser currentUser, ITokenService token, ILogger<RegisterDomainHandler> logger)
    {
        _domains = domains;
        _currentUser = currentUser;
        _token = token;
        _logger = logger;
    }

    public async Task<Result<RegisterDomainResponse>> Handle(RegisterDomainCommand cmd, CancellationToken ct)
    {
        var domain = cmd.Domain.ToLowerInvariant();

        if (!IsValidDomain(domain))
            return Result<RegisterDomainResponse>.Failure(Error.Validation("Invalid domain name"));

        var userId = _currentUser.UserId;

        var unverifiedCount = await _domains.CountPending(_currentUser.UserId, ct);
        if (unverifiedCount >= 20)
            return Result<RegisterDomainResponse>.Failure(Error.RateLimited("You have reached the maximum of 20 pending domains. Verify or remove existing ones before adding more."));

        var existing = await _domains.GetByNameAndUser(domain, userId, ct);

        if (existing is not null)
            return Result<RegisterDomainResponse>.Failure(Error.Conflict("Domain already added"));

        var (rawToken, tokenHash) = _token.Generate();

        var record = ScannedDomain.Create(
            _currentUser.UserId,
            domain,
            tokenHash);


        await _domains.AddAsync(record, ct);
        await _domains.SaveChangesAsync(ct);
        return Result<RegisterDomainResponse>.Success(RegisterDomainResponse.Create(rawToken, record));
    }

    private static bool IsValidDomain(string name) =>
          Uri.CheckHostName(name) == UriHostNameType.Dns;
}
