using Application.Features.Monitoring.DTOs;
using Application.Features.Monitoring.Mappings;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using MediatR;

namespace Application.Features.Monitoring;

public record GetDomainSettingsQuery(Guid DomainId)
    : IRequest<Result<MonitoringSettingsDto>>;

public class GetDomainSettingsHandler(
    IDomainRepository domains,
    IDomainSettingsRepository settings,
    ICurrentUser currentUser)
    : IRequestHandler<GetDomainSettingsQuery, Result<MonitoringSettingsDto>>
{
    public async Task<Result<MonitoringSettingsDto>> Handle(
        GetDomainSettingsQuery query, CancellationToken ct)
    {
        var domain = await domains.FindUserDomainById(
            currentUser.UserId, query.DomainId, ct);

        if (domain is null)
            return Result<MonitoringSettingsDto>.Failure(
                Error.NotFound("Domain not found."));

        var s = await settings.GetByDomainId(query.DomainId, ct)
             ?? DomainSettings.CreateDefault(query.DomainId);

        return Result<MonitoringSettingsDto>.Success(
            MonitoringSettingsMappings.ToDto(s, domain.DomainName));
    }
}