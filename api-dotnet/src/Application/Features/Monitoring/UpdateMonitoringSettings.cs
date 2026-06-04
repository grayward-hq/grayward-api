using Application.Features.Monitoring.DTOs;
using Application.Features.Monitoring.Mappings;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using MediatR;

namespace Application.Features.Monitoring;

public record UpdateMonitoringSettingsCommand(
    Guid DomainId,
    bool MonitoringEnabled,
    ScanFrequency ScanFrequency,
    List<int> SslAlertThresholds,
    List<AlertChannel> NotificationChannels)
    : IRequest<Result<MonitoringSettingsDto>>;

public class UpdateMonitoringSettingsHandler(
    IDomainRepository domains,
    IDomainSettingsRepository settingsRepo,
    ICurrentUser currentUser)
    : IRequestHandler<UpdateMonitoringSettingsCommand, Result<MonitoringSettingsDto>>
{
    public async Task<Result<MonitoringSettingsDto>> Handle(
        UpdateMonitoringSettingsCommand cmd, CancellationToken ct)
    {
        var domain = await domains.FindUserDomainById(
            currentUser.UserId, cmd.DomainId, ct);

        if (domain is null)
            return Result<MonitoringSettingsDto>.Failure(
                Error.NotFound("Domain not found."));

        if (domain.VerificationStatus != VerificationStatus.Verified)
            return Result<MonitoringSettingsDto>.Failure(
                Error.Forbidden(
                    "Monitoring can only be configured for verified domains."));

        var existing = await settingsRepo.GetByDomainId(cmd.DomainId, ct);

        if (existing is null)
        {
            existing = DomainSettings.CreateDefault(cmd.DomainId);
            await settingsRepo.AddAsync(existing, ct);
        }

        try
        {
            var combined = cmd.NotificationChannels
                .Aggregate(AlertChannel.None, (acc, c) => acc | c);

            existing.UpdateSettings(
                cmd.MonitoringEnabled,
                cmd.ScanFrequency,
                cmd.SslAlertThresholds,
                combined);
        }
        catch (ArgumentException ex)
        {
            return Result<MonitoringSettingsDto>.Failure(
                Error.Validation(ex.Message));
        }

        await settingsRepo.SaveChangesAsync(ct);

        return Result<MonitoringSettingsDto>.Success(
            MonitoringSettingsMappings.ToDto(existing, domain.DomainName));
    }
}
