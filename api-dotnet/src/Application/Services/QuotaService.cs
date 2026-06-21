
using Application.Interfaces;
using Domain.Common;
using Domain.Enums;

namespace Application.Services;

public class QuotaService(
        ISubscriptionRepository subs,
        IMonitoredRepoRepository repos,
        IDomainRepository domains,
        IScanRepository scans,
        IPlanCatalog plans) : IQuotaService
{
    public async Task EnsureCanOnboard(Guid userId, ResourceKind kind, CancellationToken ct)
    {
        var subscription = await subs.GetActiveByUser(userId, ct);

        if (subscription is null)
        {
            throw new ForbiddenException("No active subscription found.");
        }

        var limits = plans.Get(subscription.Plan).For(kind);

        var count = kind == ResourceKind.Repository
            ? await repos.CountUserRepositories(userId, ct)
            : await domains.CountUserDomains(userId, ct);

        if (count >= limits.MaxOnboarded)
        {
            throw new QuotaExceededException(
                QuotaKind.MaxOnboarded,
                kind,
                limits.MaxOnboarded);
        }

    }

    public async Task EnsureCanStartScan(
        Guid userId,
        ResourceKind kind,
        CancellationToken ct)
    {
        var subscription = await subs.GetActiveByUser(userId, ct)
            ?? throw new ForbiddenException("No active subscription found.");

        var limits = plans.Get(subscription.Plan).For(kind);

        var now = DateTimeOffset.UtcNow;
        var startOfMonth = new DateTimeOffset(
            now.Year,
            now.Month,
            1,
            0, 0, 0,
            TimeSpan.Zero);

        var scansThisMonth = kind == ResourceKind.Repository
            ? await scans.CountUserRepositoryScansInPeriod(
                userId,
                startOfMonth,
                now,
                ct)
            : await scans.CountUserDomainScansInPeriod(
                userId,
                startOfMonth,
                now,
                ct);

        if (scansThisMonth >= limits.MaxScansPerMonth)
        {
            throw new QuotaExceededException(
                QuotaKind.MonthlyScans,
                kind,
                limits.MaxScansPerMonth);
        }

        var activeScans = kind == ResourceKind.Repository
            ? await scans.CountUserActiveRepositoryScans(userId, ct)
            : await scans.CountUserActiveDomainScans(userId, ct);

        if (activeScans >= limits.MaxConcurrentScans)
        {
            throw new QuotaExceededException(
                QuotaKind.ConcurrentScans,
                kind,
                limits.MaxConcurrentScans);
        }
    }
}