using Application.Interfaces;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Web.Workers.Reapers;

/// <summary>
/// Periodically marks domain verification records as Revoked when they have been
/// pending for longer than 72 hours without the user completing DNS verification.
/// Runs once per 6 hours.
/// </summary>
public sealed class DomainVerificationReaper(
    ILogger<DomainVerificationReaper> logger,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan ExpiryWindow  = TimeSpan.FromHours(72);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ReapExpiredVerifications(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "DomainVerificationReaperWorker tick failed");
            }

            await Task.Delay(CheckInterval, ct);
        }
    }

    private async Task ReapExpiredVerifications(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - ExpiryWindow;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IVulnWatchDbContext>();

        var expired = await db.Domains
            .Where(d => d.VerificationStatus == VerificationStatus.Pending
                     && d.TokenIssuedAt < cutoff)
            .ToListAsync(ct);

        if (expired.Count == 0)
        {
            logger.LogDebug("DomainVerificationReaper — no expired verifications found");
            return;
        }

        foreach (var domain in expired)
        {
            domain.Revoke();
            logger.LogInformation(
                "Domain verification expired for {DomainName} (id: {DomainId}) — marked as Revoked",
                domain.DomainName, domain.Id);
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "DomainVerificationReaper reaped {Count} expired pending domain(s)",
            expired.Count);
    }
}