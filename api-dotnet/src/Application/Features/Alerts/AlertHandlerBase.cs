using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Features.Alerts;

internal abstract class AlertHandlerBase(
    IAlertRepository alerts,
    IDomainSettingsRepository domainSettings)
{
    protected readonly IAlertRepository Alerts = alerts;
    protected readonly IDomainSettingsRepository DomainSettings = domainSettings;

    protected async Task<List<AlertChannel>> ResolveChannelsAsync(
        Guid domainId, CancellationToken ct)
    {
        var settings = await DomainSettings.GetByDomainId(domainId, ct);
        return settings is not null
            ? ResolveDomainChannels(settings.NotificationChannel)
            : [AlertChannel.Email];
    }

    protected async Task SaveAlertGuarded(Alert alert, CancellationToken ct)
    {
        await Alerts.AddAsync(alert, ct);
        try
        {
            await Alerts.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            Alerts.DetachUnsavedAlerts();
        }
    }

    private static List<AlertChannel> ResolveDomainChannels(AlertChannel channel)
    {
        var channels = new List<AlertChannel>();
        if (channel.HasFlag(AlertChannel.Email)) channels.Add(AlertChannel.Email);
        if (channel.HasFlag(AlertChannel.Slack)) channels.Add(AlertChannel.Slack);
        if (channel.HasFlag(AlertChannel.Push))  channels.Add(AlertChannel.Push);
        return channels.Count > 0 ? channels : [AlertChannel.Email];
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner?.GetType().FullName != "Npgsql.PostgresException") return false;

        var sqlState = inner.GetType()
            .GetProperty("SqlState")?
            .GetValue(inner) as string;
        return sqlState == "23505";
    }
}