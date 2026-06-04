using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace Web.Workers.Monitoring;

public sealed class OwnershipCheckService(
    IDnsResolver dnsResolver,
    // IDomainRepository domainRepo,
    IConfiguration config,
    ILogger<OwnershipCheckService> logger)
{
    public async Task CheckAsync(DomainSettings settings, CancellationToken ct)
    {
        if (!config.GetValue<bool>("Dns:Lookup"))
            return;

        var domain = settings.Domain;

        if (domain.VerificationStatus != VerificationStatus.Verified)
            return;

        var txtHost = $"_vulnwatch-verify.{domain.DomainName}";
        var txtValues = await dnsResolver.GetTxtRecords(txtHost, ct);
        
        var recordStillPresent = txtValues.Any(v =>
            v.StartsWith("vulnscan-verify=", StringComparison.OrdinalIgnoreCase));

        if (recordStillPresent)
        {
            logger.LogDebug("Ownership check passed for {Domain}", domain.DomainName);
            return;
        }

        logger.LogWarning(
            "Ownership TXT record no longer found for {Domain}",
            domain.DomainName);

        // Future: dispatch DomainOwnershipLostEvent → alert user
    }
}