using Domain.Enums;

namespace Domain.Entities;

public class MonitoredRepository : EntityBase
{
    public long RepoId { get; private set; }
    public Guid UserId { get; private set; }
    public string FullName { get; private set; } = default!;
    public string CloneUrl { get; private set; } = default!;
    public string DefaultBranch { get; private set; } = default!;
    public bool IsPrivate { get; private set; } = false;
    public string InstallationId { get; private set; } = default!;  
    public RepositoryStatus Status { get; private set; } = RepositoryStatus.PendingVerification;
    public DateTimeOffset? LastScanCompletedAt { get; private set; }
    public RepositorySetting Settings { get; private set; } = null!;
    public User User { get; private set; } = default!;
    public ICollection<Scan> Scans { get; private set; } = new List<Scan>();
    private MonitoredRepository() { }
    public static MonitoredRepository Create(long repoId, Guid userId, string fullName, string cloneUrl, string installationId, bool isPrivate, string defaultBranch = "main")
    {
        var repo = new MonitoredRepository
        {
            RepoId = repoId,
            UserId = userId,
            FullName = fullName,
            CloneUrl = cloneUrl,
            InstallationId = installationId,
            DefaultBranch = defaultBranch,
            IsPrivate = isPrivate,
            Status = RepositoryStatus.PendingVerification
        };
        repo.Settings = RepositorySetting.CreateDefault(repo.Id);
        return repo;
    }

    public bool EnsureSettings()
    {
        if (Settings is not null)
            return false;

        Settings = RepositorySetting.CreateDefault(Id);
        return true;
    }

    public void UpdateFromGitHub(
        string fullName,
        string installationId,
        string defaultBranch,
        bool isPrivate,
        string htmlUrl)
    {
        FullName = fullName;
        InstallationId = installationId;
        DefaultBranch = defaultBranch;
        CloneUrl = htmlUrl;
        IsPrivate = isPrivate;
        Touch();
    }

    public void Suspend()
    {
        Status = RepositoryStatus.Suspended;
        Touch();
    }

    public void Activate()
    {
        Status = RepositoryStatus.Active;
        Touch();
    }
}
