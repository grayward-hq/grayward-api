using System.Text.Json;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public class VulnWatchDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>, IVulnWatchDbContext, IDataProtectionKeyContext
{
    public VulnWatchDbContext(DbContextOptions<VulnWatchDbContext> options)
        : base(options) { }

    // Users table is provided by IdentityDbContext
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Waitlist> Waitlists => Set<Waitlist>();
    public DbSet<BrandThreat> BrandThreats => Set<BrandThreat>();
    public DbSet<ScannedDomain> Domains => Set<ScannedDomain>();
    public DbSet<Scan> Scans => Set<Scan>();
    public DbSet<Finding> Findings => Set<Finding>();
    public DbSet<Remediation> Remediations => Set<Remediation>();
    public DbSet<Integration> Integrations => Set<Integration>();
    public DbSet<MonitoredEmail> MonitoredEmails => Set<MonitoredEmail>();
    public DbSet<OwaspMapping> OwaspMappings => Set<OwaspMapping>();
    public DbSet<MonitoredRepository> MonitoredRepositories => Set<MonitoredRepository>();
    public DbSet<NotificationPreferences> NotificationPreferences => Set<NotificationPreferences>();
    public DbSet<WebHookOutBox> WebHookOutBox => Set<WebHookOutBox>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<DomainSettings> DomainSettings => Set<DomainSettings>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // must call base — sets up Identity tables

        builder.Entity<User>(e =>
        {
            // Identity already handles Email uniqueness; only add custom index
            e.Property(x => x.FirstName)
               .HasMaxLength(100)
               .IsRequired(false);

            e.Property(x => x.LastName)
                   .HasMaxLength(100)
                   .IsRequired(false);

            e.HasIndex(u => u.GoogleId).IsUnique().HasFilter("\"GoogleId\" IS NOT NULL");
        });

        builder.Entity<RefreshToken>(e =>
        {
            e.HasKey(t => t.Id);

            e.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(512);

            e.HasIndex(t => t.TokenHash).IsUnique();

            e.HasIndex(t => t.UserId);

            e.Property(t => t.CreatedByIp).HasMaxLength(45);

            e.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ScannedDomain>(e =>
        {
            e.Property(d => d.VerificationStatus).HasConversion<string>();
            e.HasOne(d => d.User)
             .WithMany()
             .HasForeignKey(d => d.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => new { d.UserId, d.DomainName }).IsUnique();
            e.HasIndex(d => new { d.DomainName, d.VerificationStatus });
            e.HasIndex(d => new { d.UserId, d.VerificationStatus });
            e.HasIndex(d => new { d.VerificationStatus, d.SslCertExpiry })
                .HasFilter("\"VerificationStatus\" = 'Verified' AND \"SslCertExpiry\" IS NOT NULL")
                .HasDatabaseName("IX_ScannedDomains_Verified_SslCertExpiry");
        });

        builder.Entity<DomainSettings>(e =>
        {
            e.HasKey(s => s.Id);

            e.Property(s => s.ScanFrequency).HasConversion<string>();
            e.Property(s => s.NotificationChannel).HasConversion<int>();

            // Store thresholds as a plain string column — no JSON needed
            e.Property(s => s.SslAlertThresholds)
                .HasColumnName("SslAlertThresholds")
                .HasMaxLength(50)
                .IsRequired();

            e.HasOne(s => s.Domain)
                .WithOne()
                .HasForeignKey<DomainSettings>(s => s.DomainId)
                .OnDelete(DeleteBehavior.Cascade);

            // One settings row per domain — enforced at DB level
            e.HasIndex(s => s.DomainId)
                .IsUnique()
                .HasDatabaseName("IX_DomainSettings_DomainId");

            // Worker query — find everything due for scanning
            e.HasIndex(s => new { s.MonitoringEnabled, s.NextScheduledAt })
                .HasDatabaseName("IX_DomainSettings_DueForScan")
                .HasFilter("\"MonitoringEnabled\" = true");
        });

        builder.Entity<MonitoredEmail>(e =>
        {
           e.HasKey(e => e.Id);

            e.Property(e => e.EmailAddress)
                .IsRequired()
                .HasMaxLength(254); // RFC 5321 max email length

            e.Property(e => e.IsBreached)
                .IsRequired()
                .HasDefaultValue(false);

            e.Property(e => e.BreachCount)
                .IsRequired()
                .HasDefaultValue(0);

            e.Property(e => e.LastCheckedAt);
            e.Property(e => e.LatestDetectionAt);

            // One email address per domain — no duplicates
            e.HasIndex(e => new { e.DomainId, e.EmailAddress })
                .IsUnique()
                .HasDatabaseName("IX_MonitoredEmails_DomainId_EmailAddress");

            // For fetching all emails due for a check
            e.HasIndex(e => e.DomainId)
                .HasDatabaseName("IX_MonitoredEmails_DomainId");

            // For fetching all monitored emails for a user across domains
            e.HasIndex(e => e.UserId)
                .HasDatabaseName("IX_MonitoredEmails_UserId");

            e.HasOne<ScannedDomain>()
                .WithMany(d => d.MonitoredEmails)
                .HasForeignKey(e => e.DomainId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OwaspMapping>(e =>
        {

            e.HasKey(x => x.Id);

            e.Property(x => x.ScanId)
                .IsRequired();

            e.Property(x => x.FindingId)
                .IsRequired();

            e.Property(x => x.CategoryCode)
                .HasMaxLength(50)
                .IsRequired();

            e.Property(x => x.CategoryName)
                .HasMaxLength(200)
                .IsRequired();

            e.Property(x => x.FindingLabel)
                .HasMaxLength(500);

            e.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            e.Property(x => x.Severity)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            e.HasOne(x => x.Scan)
                .WithMany()
                .HasForeignKey(x => x.ScanId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Finding)
                .WithMany()
                .HasForeignKey(x => x.FindingId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new
            {
                x.ScanId,
                x.FindingId,
                x.CategoryCode
            }).IsUnique();
        });

        builder.Entity<BrandThreat>(e =>
        {
            e.HasKey(b => b.Id);

            e.Property(b => b.LookAlikeDomain)
                .IsRequired()
                .HasMaxLength(253); // max DNS name length

            e.Property(b => b.VariationType)
                .IsRequired()
                .HasMaxLength(50);

            e.Property(b => b.ResolvedIpAddress)
                .HasMaxLength(45); // IPv6 max length

            e.Property(b => b.HttpTitle)
                .HasMaxLength(500);

            e.Property(b => b.RiskLevel)
                .HasConversion<string>()
                .HasMaxLength(20);

            e.Property(b => b.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            e.Property(b => b.LastCheckedAt)
                .IsRequired();

            // Unique constraint — prevent duplicate candidates per domain
            e.HasIndex(b => new { b.DomainId, b.LookAlikeDomain })
                .IsUnique()
                .HasDatabaseName("IX_BrandThreats_DomainId_LookAlikeDomain");

            // Index for the common query — all threats for a domain
            e.HasIndex(b => b.DomainId)
                .HasDatabaseName("IX_BrandThreats_DomainId");

            // Index for filtering active threats
            e.HasIndex(b => new { b.DomainId, b.Status })
                .HasDatabaseName("IX_BrandThreats_DomainId_Status");

            e.HasOne(b => b.Domain)
                .WithMany(d => d.BrandThreats)
                .HasForeignKey(b => b.DomainId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Scan>(e =>
        {
            e.HasIndex(s => s.IdempotencyKey).IsUnique();
            e.Property(s => s.TargetType).HasConversion<string>();
            e.Property(s => s.Status).HasConversion<string>();
            e.HasOne(s => s.Domain)
             .WithMany(d => d.Scans)
             .HasForeignKey(s => s.DomainId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Repository)
                .WithMany(r => r.Scans)
                .HasForeignKey(s => s.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.User)
             .WithMany()
             .HasForeignKey(s => s.UserId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(s => new { s.DomainId, s.Status });
            e.HasIndex(s => new { s.UserId, s.Status });
            e.HasIndex(s => s.DomainId)
                .HasFilter("\"Status\" IN ('Queued', 'Running')")
                .HasDatabaseName("IX_Scans_DomainId_Active");
        });

        builder.Entity<Finding>(e =>
        {
            e.Property(f => f.Surface).HasConversion<string>();
            e.Property(f => f.Severity).HasConversion<string>();
            e.Property(f => f.Status).HasConversion<string>();
            e.HasOne(f => f.Scan)
             .WithMany(s => s.Findings)
             .HasForeignKey(f => f.ScanId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(f => new { f.ScanId, f.Severity, f.Status })
             .HasDatabaseName("IX_Findings_ScanId_Severity_Status");
        });

        builder.Entity<Remediation>(e =>
        {
            e.Property(r => r.Status).HasConversion<string>();
            e.HasOne(r => r.Finding)
             .WithMany()
             .HasForeignKey(r => r.FindingId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => new { r.FindingId, r.Status })
             .HasDatabaseName("IX_Remediations_FindingId_Status");
        });

        builder.Entity<Integration>(e =>
        {
            e.Property(i => i.Status).HasConversion<string>();
            e.Property(i => i.Provider).HasConversion<string>();
            e.HasOne(i => i.User)
             .WithMany()
             .HasForeignKey(i => i.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(i => new { i.UserId, i.Status })
             .HasDatabaseName("IX_Integrations_UserId_Status");

            var converter = new ValueConverter<Dictionary<string, string>, string>(
                v => v == null || v.Count == 0
                    ? "{}"
                    : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrWhiteSpace(v)
                    ? new Dictionary<string, string>()
                    : TryDeserialize(v));

            var comparer = new ValueComparer<Dictionary<string, string>>(
                (a, b) => JsonSerializer.Serialize(a ?? new(), (JsonSerializerOptions?)null) ==
                        JsonSerializer.Serialize(b ?? new(), (JsonSerializerOptions?)null),
                v => v == null
                    ? 0
                    : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                v => v == null
                    ? new Dictionary<string, string>()
                    : TryDeserialize(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null)));

            e.Property(x => x.Metadata)
                .HasColumnType("jsonb")
                .HasConversion(converter, comparer);
        });

        builder.Entity<MonitoredRepository>(e =>
        {
            e.HasIndex(r => r.RepoId).IsUnique();
            e.HasOne(r => r.User)
             .WithMany()
             .HasForeignKey(r => r.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => r.UserId)
             .HasDatabaseName("IX_MonitoredRepositories_UserId");
        });

        builder.Entity<NotificationPreferences>(e =>
        {
            e.HasOne(n => n.User)
             .WithMany()
             .HasForeignKey(n => n.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(n => n.UserId)
             .IsUnique()
             .HasDatabaseName("IX_NotificationPreferences_UserId");
        });

        builder.Entity<WebHookOutBox>(e =>
        {
            e.Property(w => w.Status)
            .HasConversion<string>();
            e.HasIndex(w => new { w.Status, w.CreatedAt })
            .HasFilter("\"Status\" = 'Pending'")
            .HasDatabaseName("IX_WebHookOutBox_Pending_CreatedAt");
        });

        builder.Entity<Alert>(e =>
        {
            e.HasIndex(a => new { a.UserId, a.Type, a.DomainId, a.Channel, a.DeduplicationKey })
                .IsUnique()
                .HasDatabaseName("IX_Alerts_Deduplication");

            e.Property(a => a.Status).HasConversion<string>();

            e.HasIndex(a => new { a.UserId, a.CreatedAt })
            .HasDatabaseName("IX_Alerts_UserId_CreatedAt");

            e.HasIndex(a => new { a.Channel, a.CreatedAt })
            .HasFilter("\"Status\" = 'Pending'")
            .HasDatabaseName("IX_Alerts_Pending_Channel_CreatedAt");
        });
    }

    private static Dictionary<string, string> TryDeserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, (JsonSerializerOptions?)null)
                ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }
}
