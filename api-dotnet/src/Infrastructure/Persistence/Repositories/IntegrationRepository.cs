using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class IntegrationRepository(VulnWatchDbContext db)
    : BaseRepository<Integration>(db), IIntegrationRepository
{
    public Task<Integration?> GetByUserAndProvider(
        Guid userId, IntegrationProvider provider, CancellationToken ct) =>
        Db.Integrations
            .FirstOrDefaultAsync(i =>
                i.UserId == userId &&
                i.Provider == provider && i.Status == IntegrationStatus.ACTIVE, ct);
}