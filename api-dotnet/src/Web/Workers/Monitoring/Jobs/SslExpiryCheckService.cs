using Application.Features.Alerts;
using Domain.Entities;
using Domain.Events;
using Microsoft.Extensions.Logging;

namespace Web.Workers.Monitoring;

public sealed class SslExpiryCheckService(
    AlertDispatcher alertDispatcher,
    ILogger<SslExpiryCheckService> logger)
{
    public async Task CheckAsync(
        DomainSettings settings,
        CancellationToken ct)
    {
        var domain = settings.Domain;

        if (domain.SslCertExpiry is null)
        {
            logger.LogDebug(
                "No SSL expiry recorded for {Domain} — skipping",
                domain.DomainName);
            return;
        }

        var todayUtc      = DateTime.UtcNow.Date;
        var daysRemaining = (domain.SslCertExpiry.Value.UtcDateTime.Date
                            - todayUtc).Days;

        // Use this domain's configured thresholds, not the global hardcoded ones
        var thresholds = settings.GetSslAlertThresholds();

        if (!thresholds.Contains(daysRemaining))
            return;

        logger.LogInformation(
            "SSL expiry alert threshold hit for {Domain} — {Days} days remaining",
            domain.DomainName, daysRemaining);

        await alertDispatcher.DispatchAsync(new SslExpiryEvent(
            DomainId:      domain.Id,
            UserId:        domain.UserId,
            DomainName:    domain.DomainName,
            ExpiresAt:     domain.SslCertExpiry.Value.UtcDateTime,
            DaysRemaining: daysRemaining), ct);
    }
}