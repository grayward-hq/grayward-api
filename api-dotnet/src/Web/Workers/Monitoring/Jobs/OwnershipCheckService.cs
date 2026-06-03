using Application.Features.Alerts;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Events;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace Web.Workers.Monitoring;

public sealed class OwnershipCheckService(
    IDnsResolver dnsResolver,
    IDomainSettingsRepository settingsRepo,
    AlertDispatcher alertDispatcher,
    IConfiguration config,
    ILogger<OwnershipCheckService> logger)
{
    private static readonly TimeSpan AlertThreshold   = TimeSpan.FromHours(24);
    private static readonly TimeSpan PauseThreshold   = TimeSpan.FromHours(72);
    private static readonly TimeSpan RevokeThreshold  = TimeSpan.FromDays(7);

    public async Task CheckAsync(DomainSettings settings, CancellationToken ct)
    {
        if (!config.GetValue<bool>("Dns:Lookup"))
            return;

        var domain = settings.Domain;

        if (domain.VerificationStatus != VerificationStatus.Verified)
            return;

        var txtHost   = $"_vulnwatch-verify.{domain.DomainName}";
        var txtValues = await dnsResolver.GetTxtRecords(txtHost, ct);

        var recordPresent = txtValues.Any(v =>
            v.StartsWith("vulnscan-verify=", StringComparison.OrdinalIgnoreCase));

        if (recordPresent)
        {
            settings.RecordOwnershipConfirmed();
            await settingsRepo.SaveChangesAsync(ct);
            logger.LogDebug("Ownership check passed for {Domain}", domain.DomainName);
            return;
        }

        // Record failed
        settings.RecordOwnershipCheckFailed();
        await settingsRepo.SaveChangesAsync(ct);

        var failedDuration = DateTime.UtcNow - settings.OwnershipFailedSince!.Value;

        logger.LogWarning(
            "Ownership TXT record missing for {Domain} — failing for {Duration}",
            domain.DomainName, failedDuration);

        if (failedDuration >= RevokeThreshold)
        {
            await HandleRevoke(domain, settings, ct);
        }
        else if (failedDuration >= PauseThreshold)
        {
            await HandlePause(domain, settings, alertDispatcher, ct);
        }
        else if (failedDuration >= AlertThreshold)
        {
            await HandleAlert(domain, settings, alertDispatcher, ct);
        }
        // else: < 24h, just logged — DNS might still be propagating
    }

    private async Task HandleAlert(
        ScannedDomain domain,
        DomainSettings settings,
        AlertDispatcher alertDispatcher,
        CancellationToken ct)
    {
        logger.LogWarning(
            "Ownership alert threshold reached for {Domain} — sending alert",
            domain.DomainName);

        await alertDispatcher.DispatchAsync(new DomainOwnershipWarningEvent(
            DomainId:   domain.Id,
            UserId:     domain.UserId,
            DomainName: domain.DomainName,
            FailedSince: settings.OwnershipFailedSince!.Value,
            Stage:      OwnershipWarningStage.Warning), ct);
    }

    private async Task HandlePause(
        ScannedDomain domain,
        DomainSettings settings,
        AlertDispatcher alertDispatcher,
        CancellationToken ct)
    {
        if (!settings.MonitoringEnabled)
            return; 

        logger.LogWarning(
            "Pausing monitoring for {Domain} — TXT record missing for 72h",
            domain.DomainName);

        settings.Disable();
        await settingsRepo.SaveChangesAsync(ct);

        await alertDispatcher.DispatchAsync(new DomainOwnershipWarningEvent(
            DomainId:   domain.Id,
            UserId:     domain.UserId,
            DomainName: domain.DomainName,
            FailedSince: settings.OwnershipFailedSince!.Value,
            Stage:      OwnershipWarningStage.MonitoringPaused), ct);
    }

    private async Task HandleRevoke(
        ScannedDomain domain,
        DomainSettings settings,
        CancellationToken ct)
    {
        logger.LogWarning(
            "Revoking domain {Domain} — TXT record missing for 7 days",
            domain.DomainName);

        domain.Revoke();

        await alertDispatcher.DispatchAsync(new DomainOwnershipWarningEvent(
            DomainId:   domain.Id,
            UserId:     domain.UserId,
            DomainName: domain.DomainName,
            FailedSince: settings.OwnershipFailedSince!.Value,
            Stage:      OwnershipWarningStage.Revoked), ct);
    }
}
