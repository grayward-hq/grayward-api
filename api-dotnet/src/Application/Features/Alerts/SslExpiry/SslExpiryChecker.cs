using Domain.Enums;
using Domain.Events;
using Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Application.Features.Alerts.SslExpiry;

public sealed class SslExpiryChecker
{
    private static readonly int[] ThresholdDays = [30, 14, 7];

    private readonly IDomainRepository _domainRepository;
    private readonly ILogger<SslExpiryChecker> _logger;

    public SslExpiryChecker(IDomainRepository domainRepository, ILogger<SslExpiryChecker> logger)
    {
        _domainRepository = domainRepository;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SslExpiryEvent>> GetPendingAlerts(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var todayUtc = now.UtcDateTime.Date;
        var maxLookahead = now.AddDays(ThresholdDays.Max() + 1);

        // _logger.LogInformation("Querying domains with SSL expiry between {Now} and {MaxLookahead}", now, maxLookahead);

        var domains = await _domainRepository
            .GetDomainsWithExpiringCertificates(maxLookahead, ct);

        // _logger.LogInformation("Repository returned {Count} domains with expiring certificates", domains.Count);

        if (domains.Count == 0)
        {
            // _logger.LogInformation("No domains found with SSL certs expiring before {MaxLookahead}", maxLookahead);
            return [];
        }

        // foreach (var d in domains)
        // {
        //     _logger.LogDebug(
        //         "Domain {DomainName} (Id: {DomainId}) — SSL expiry: {Expiry}, Days remaining: {Days}",
        //         d.DomainName, d.Id, d.SslCertExpiry, (d.SslCertExpiry!.Value - now).Days);
        // }

        var events = domains
            .Select(d => new
            {
                Domain = d,
                DaysRemaining = (d.SslCertExpiry!.Value.UtcDateTime.Date - todayUtc).Days
            })
            .Where(x => ThresholdDays.Contains(x.DaysRemaining))
            .Select(x => new SslExpiryEvent(
                DomainId: x.Domain.Id,
                UserId: x.Domain.UserId,
                DomainName: x.Domain.DomainName,
                ExpiresAt: x.Domain.SslCertExpiry!.Value.UtcDateTime,
                DaysRemaining: x.DaysRemaining))
            .ToList();

        // _logger.LogInformation(
        //     "{EventCount} of {DomainCount} domains matched threshold days [{Thresholds}]",
        //     events.Count, domains.Count, string.Join(", ", ThresholdDays));

        return events;
    }
}