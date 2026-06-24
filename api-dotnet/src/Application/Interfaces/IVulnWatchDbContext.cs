using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
namespace Application.Interfaces;

public interface IVulnWatchDbContext
{
    DbSet<Alert> Alerts { get; }
    DbSet<ScannedDomain> Domains { get; }
    DbSet<DomainSettings> DomainSettings { get; }
    DbSet<Scan> Scans { get; }
    DbSet<Finding> Findings { get; }
    DbSet<MonitoredRepository> MonitoredRepositories { get; }
    DbSet<RepositorySetting> RepositorySettings { get; }

    Task<int> SaveChangesAsync(CancellationToken ct);
    DatabaseFacade Database { get; }

    void SetOriginalVersion<TEntity>(TEntity entity, uint version) where TEntity : class;

}