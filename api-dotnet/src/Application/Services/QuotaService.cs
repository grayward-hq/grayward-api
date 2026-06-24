
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

public class QuotaService(
        ISubscriptionRepository subs,
        IMonitoredRepoRepository repos,
        IDomainRepository domains,
        IScanRepository scans,
        IPlanCatalog plans,
        IUnitOfWork uow) : IQuotaService
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

    public Task<Result<Scan>> ReserveScanSlot(Guid userId, ResourceKind kind, Scan scan, CancellationToken ct)
    => uow.InTransaction(async token =>
    {
        var subscription = await subs.GetActiveByUserForUpdate(userId, token);
        if (subscription is null)
            return Result<Scan>.Failure(Error.Forbidden("No active subscription."));


        var limits = plans.Get(subscription.Plan).For(kind);

        var activeScans = kind == ResourceKind.Repository
            ? await scans.CountUserActiveRepositoryScans(userId, token)
            : await scans.CountUserActiveDomainScans(userId, token);

        if (activeScans >= limits.MaxConcurrentScans)
            return Result<Scan>.Failure(
                Error.RateLimited($"Concurrent scan limit reached ({limits.MaxConcurrentScans})."));

        await scans.AddAsync(scan, token);
        await uow.SaveChanges(token);

        return Result<Scan>.Success(scan);
    }, ct);

}