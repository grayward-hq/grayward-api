namespace Domain.Entities;

public class UsageMetric : EntityBase
{
    public Guid UserId { get; private set; }
    public DateOnly Date { get; private set; }
    public int DomainScanCount { get; private set; }
    public int DomainScanMinutesConsumed { get; private set; }

    public int RepositoryScanCount { get; private set; }
    public int RepositoryScanMinutesConsumed { get; private set; }

    public User User { get; private set; } = default!;

    private UsageMetric() { }

    public static UsageMetric Create(Guid userId, DateOnly date)
        => new()
        {
            UserId = userId,
            Date = date,
            DomainScanCount = 0,
            DomainScanMinutesConsumed = 0,
            RepositoryScanCount = 0,
            RepositoryScanMinutesConsumed = 0
        };

    public void RecordDomainScan(int minutesConsumed)
    {
        if (minutesConsumed < 0)
            throw new ArgumentOutOfRangeException(nameof(minutesConsumed));

        DomainScanCount++;
        DomainScanMinutesConsumed += minutesConsumed;
    }

    public void RecordRepositoryScan(int minutesConsumed)
    {
        if (minutesConsumed < 0)
            throw new ArgumentOutOfRangeException(nameof(minutesConsumed));

        RepositoryScanCount++;
        RepositoryScanMinutesConsumed += minutesConsumed;
    }
}