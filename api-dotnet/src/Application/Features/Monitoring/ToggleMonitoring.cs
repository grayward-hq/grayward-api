using Application.Features.Monitoring.DTOs;
using Application.Interfaces;
using Application.Features.Monitoring.Mappings;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using MediatR;

namespace Application.Features.Monitoring;

public record ToggleMonitoringCommand(Guid DomainId, bool Enable)
    : IRequest<Result<MonitoringSettingsDto>>;

public class ToggleMonitoringHandler(
    IDomainRepository domains,
    IDomainSettingsRepository settingsRepo,
    ICurrentUser currentUser)
    : IRequestHandler<ToggleMonitoringCommand, Result<MonitoringSettingsDto>>
{
    public async Task<Result<MonitoringSettingsDto>> Handle(
        ToggleMonitoringCommand cmd, CancellationToken ct)
    {
        var domain = await domains.FindUserDomainById(
            currentUser.UserId, cmd.DomainId, ct);

        if (domain is null)
            return Result<MonitoringSettingsDto>.Failure(
                Error.NotFound("Domain not found."));

        var existing = await settingsRepo.GetByDomainId(cmd.DomainId, ct);

        if (existing is null)
        {
            // First toggle — seed defaults then apply
            existing = DomainSettings.CreateDefault(cmd.DomainId);
            await settingsRepo.AddAsync(existing, ct);
        }

        if (cmd.Enable)
            existing.Enable();
        else
            existing.Disable();

        await settingsRepo.SaveChangesAsync(ct);

        return Result<MonitoringSettingsDto>.Success(
            MonitoringSettingsMappings.ToDto(existing, domain.DomainName));
    }
}
