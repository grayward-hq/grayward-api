using System.Security.Cryptography;
using System.Text;
using Application.Features.Domain.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;


namespace Application.Features.Domain;

public record VerifyDomainCommand(Guid DomainId) : IRequest<Result<VerifyDomainResponse>>;

public class VerifyDomainHandler(
    IDomainRepository domains,
    IDomainSettingsRepository monitoringSettings,
    ICurrentUser currentUser,
    IDnsResolver dnsResolver,
    ILogger<VerifyDomainHandler> logger,
    IConfiguration config
) : IRequestHandler<VerifyDomainCommand, Result<VerifyDomainResponse>>
{
    public async Task<Result<VerifyDomainResponse>> Handle(
        VerifyDomainCommand cmd,
        CancellationToken ct)
    {
        var record = await domains.GetById(cmd.DomainId, ct);

        if (record is null || record.UserId != currentUser.UserId)
            return Result<VerifyDomainResponse>.Failure(
                Error.NotFound("Domain not found."));

        if (record.VerificationStatus == VerificationStatus.Verified)
            return Result<VerifyDomainResponse>.Failure(
                Error.Conflict("Domain is already verified."));

        if (config.GetValue<bool>("Dns:Lookup"))
        {
            var txtHost = $"_vulnwatch-verify.{record.DomainName}";
            var txtValues = await dnsResolver.GetTxtRecords(txtHost, ct);

            foreach (var value in txtValues)
            {
                logger.LogInformation(
                    "Resolved TXT record for {Host}: {Value}",
                    txtHost, value);
            }

            var expectedHash = record.VerificationToken;
            var matchFound = txtValues.Any(v =>
            {
                var hash = Convert.ToBase64String(
                    SHA256.HashData(Encoding.UTF8.GetBytes(v)));
                return hash == expectedHash;
            });

            if (!matchFound)
                return Result<VerifyDomainResponse>.Success(
                    new VerifyDomainResponse(
                        Status: VerificationStatus.Pending,
                        Message: "TXT record not found yet — DNS may still be propagating."));
        }
        else
        {
            logger.LogWarning(
                "DNS lookup bypassed for domain {DomainId} — Dns:Lookup is disabled.",
                cmd.DomainId);
        }

        record.Verify();

        try
        {
            var existing = await monitoringSettings.GetByDomainId(cmd.DomainId, ct);

            if (existing is null)
            {
                var settings = DomainSettings.CreateDefault(cmd.DomainId);  
                settings.RecordOwnershipConfirmed();  
                await monitoringSettings.AddAsync(settings, ct); 
            }
            else
            {
                existing.Enable();
                existing.RecordOwnershipConfirmed();
            }

            await domains.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (
            ex is DbUpdateException or DbUpdateConcurrencyException)
        {
            logger.LogWarning(
                "DomainSettings upsert conflict for {DomainId} — concurrent request",
                cmd.DomainId);
        }

        

        return Result<VerifyDomainResponse>.Success(
            new VerifyDomainResponse(
                Status: VerificationStatus.Verified,
                Message: "Domain verified successfully."));
    }
}