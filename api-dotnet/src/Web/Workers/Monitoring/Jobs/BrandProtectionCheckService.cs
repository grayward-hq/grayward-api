

using Application.Features.Alerts;
using Application.Helpers;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Events;

namespace Web.Workers.Monitoring.Jobs;

public class BrandProtectionCheckService(
    IBrandThreatRepository brandThreatRepo,
    LookAlikeDomainChecker checker,
    AlertDispatcher alertDispatcher,
    ILogger<BrandProtectionCheckService> logger)
{
    // Cap candidates per run to avoid hammering DNS on large domains
    private const int MaxCandidatesPerRun = 50;
    private const int DelayBetweenChecksMs = 200;

    public async Task CheckAsync(ScannedDomain domain, CancellationToken ct)
    {
        var candidates = LookAlikeDomainGenerator
            .Generate(domain.DomainName)
            .Take(MaxCandidatesPerRun)
            .ToList();

        logger.LogInformation(
            "[BrandProtection] Checking {Count} candidates for {Domain}",
            candidates.Count, domain.DomainName);

        foreach (var candidate in candidates)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await ProcessCandidateAsync(domain, candidate, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "[BrandProtection] Failed checking candidate {Candidate}", candidate.Domain);
            }

            await Task.Delay(DelayBetweenChecksMs, ct);
        }

        // Mark any previously Active threats that weren't re-checked as needing review
        await MarkStaleThreatsAsync(domain.Id, candidates.Select(c => c.Domain).ToList(), ct);
    }

    private async Task ProcessCandidateAsync(
        ScannedDomain domain, LookAlikeDomain candidate, CancellationToken ct)
    {
        var result = await checker.CheckAsync(candidate.Domain, domain.DomainName, ct);

        var existing = await brandThreatRepo
            .FindByDomainAndLookAlike(domain.Id, candidate.Domain, ct);

        if (existing is null)
        {
            var threat = BrandThreat.Create(
                domain.Id,
                candidate.Domain,
                candidate.VariationType,
                result.ResolvesViaDns,
                result.RespondedViaHttp,
                result.RedirectsToOriginal,
                result.RiskLevel,
                result.ResolvedIpAddress,
                result.HttpStatusCode,
                result.HttpTitle
            );

            await brandThreatRepo.AddAsync(threat, ct);

            // Only alert on Medium+ to avoid noise from Low/non-resolving domains
            if (result.RiskLevel >= BrandThreatRiskLevel.Medium)
            {
                await alertDispatcher.DispatchAsync(
                    new BrandThreatDetectedEvent(domain, threat), ct);

                logger.LogWarning(
                    "[BrandProtection] {Risk} threat detected: {Candidate} for {Domain}",
                    result.RiskLevel, candidate.Domain, domain.DomainName);
            }
        }
        else
        {
            var wasActive = existing.Status == BrandThreatStatus.Active;

            existing.ResolvesViaDns      = result.ResolvesViaDns;
            existing.ResolvedIpAddress   = result.ResolvedIpAddress;
            existing.RespondedViaHttp    = result.RespondedViaHttp;
            existing.HttpStatusCode      = result.HttpStatusCode;
            existing.HttpTitle           = result.HttpTitle;
            existing.RedirectsToOriginal = result.RedirectsToOriginal;
            existing.RiskLevel           = result.RiskLevel;
            existing.LastCheckedAt       = DateTime.UtcNow;

            // Threat went offline — resolve it
            if (wasActive && !result.ResolvesViaDns && !result.RespondedViaHttp)
            {
                existing.Status     = BrandThreatStatus.Resolved;
                existing.ResolvedAt = DateTime.UtcNow;

                logger.LogInformation(
                    "[BrandProtection] Threat resolved (went offline): {Candidate}",
                    candidate.Domain);
            }
            // Threat came back online after being resolved
            else if (existing.Status == BrandThreatStatus.Resolved
                     && (result.ResolvesViaDns || result.RespondedViaHttp))
            {
                existing.Status     = BrandThreatStatus.Active;
                existing.ResolvedAt = null;

                await alertDispatcher.DispatchAsync(
                    new BrandThreatDetectedEvent(domain, existing), ct);
            }

            brandThreatRepo.Update(existing);
            await brandThreatRepo.SaveChangesAsync(ct);

        }
    }

    private async Task MarkStaleThreatsAsync(
        Guid domainId, List<string> checkedCandidates, CancellationToken ct)
    {
        // Any Active threat not in this run's candidate list
        // (e.g. generator logic changed) → move to Monitoring
        var activeThreats = await brandThreatRepo.FindActiveByDomain(domainId, ct);

        foreach (var threat in activeThreats
            .Where(t => !checkedCandidates.Contains(
                t.LookAlikeDomain, StringComparer.OrdinalIgnoreCase)))
        {
            threat.Status = BrandThreatStatus.Monitoring;
            brandThreatRepo.Update(threat);

            await brandThreatRepo.SaveChangesAsync(ct);

        }
    }
}