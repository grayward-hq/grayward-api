using Application.Features.Alerts;
using Application.Features.BreachMonitoring;
using Application.Interfaces;
using Domain.Entities;
using Domain.Events;

namespace Web.Workers.Monitoring.Jobs;

public class BreachMonitoringService(
    IMonitoredEmailRepository emailRepo,
    HaveIBeenPwnedService hibpService,
    AlertDispatcher alertDispatcher,
    ILogger<BreachMonitoringService> logger)
{
    private const int MaxEmailsPerDomain = 5;
    private const int HibpDelayMs = 700; // HIBP rate limit — 1 req/1500ms on free tier

    public async Task CheckAsync(ScannedDomain domain, CancellationToken ct)
    {
        var emails = await emailRepo.GetByDomainId(domain.Id, ct);

        if (emails.Count == 0)
        {
            logger.LogDebug("[BreachMonitor] No monitored emails for {Domain}", domain.DomainName);
            return;
        }

        logger.LogInformation(
            "[BreachMonitor] Checking {Count} email(s) for {Domain}",
            emails.Count, domain.DomainName);

        foreach (var email in emails.Take(MaxEmailsPerDomain))
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await ProcessEmailAsync(domain, email, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "[BreachMonitor] Failed checking {Email}", email.EmailAddress);
            }

            await Task.Delay(HibpDelayMs, ct);
        }

        await emailRepo.SaveChangesAsync(ct);
    }

    private async Task ProcessEmailAsync(
        ScannedDomain domain, MonitoredEmail email, CancellationToken ct)
    {
        var result = await hibpService.CheckEmailAsync(email.EmailAddress, ct);

        var escalated = email.UpdateBreachStatus(result.BreachCount);

        if (escalated)
        {
            logger.LogWarning(
                "[BreachMonitor] New breach detected for {Email} — {Count} breach(es)",
                email.EmailAddress, result.BreachCount);

            await alertDispatcher.DispatchAsync(
                new CredentialBreachEvent(domain, email, result.BreachNames), ct);
        }
    }
}