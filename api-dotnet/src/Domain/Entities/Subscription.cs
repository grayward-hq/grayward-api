using Domain.Enums;

namespace Domain.Entities;

public class Subscription : EntityBase
{
    public Guid UserId { get; private set; }
    public PlanCode Plan { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset CurrentPeriodStart { get; private set; }
    public DateTimeOffset CurrentPeriodEnd { get; private set; }

    public User User { get; private set; } = default!;

    private Subscription() { }

    public static Subscription Create(
        Guid userId,
        PlanCode plan,
        DateTimeOffset currentPeriodStart,
        DateTimeOffset currentPeriodEnd)
    {
        if (currentPeriodEnd <= currentPeriodStart)
            throw new ArgumentException(
                "Current period end must be greater than current period start.");

        return new Subscription
        {
            UserId = userId,
            Plan = plan,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = currentPeriodStart,
            CurrentPeriodEnd = currentPeriodEnd
        };
    }

    public void Renew(DateTimeOffset periodStart, DateTimeOffset periodEnd)
    {
        if (periodEnd <= periodStart)
            throw new ArgumentException(
                "Current period end must be greater than current period start.");

        CurrentPeriodStart = periodStart;
        CurrentPeriodEnd = periodEnd;
        Status = SubscriptionStatus.Active;
    }

    public void ChangePlan(PlanCode plan)
    {
        Plan = plan;
    }

    public void Cancel()
    {
        Status = SubscriptionStatus.Cancelled;
    }

    public void Suspend()
    {
        Status = SubscriptionStatus.Suspended;
    }

    public void Activate()
    {
        Status = SubscriptionStatus.Active;
    }

    public bool IsActive(DateTimeOffset asOf)
        => Status == SubscriptionStatus.Active &&
           asOf >= CurrentPeriodStart &&
           asOf <= CurrentPeriodEnd;
}