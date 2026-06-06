using Domain.Common;
using Domain.Entities;
using Domain.Enums;

namespace Application.Features.BrandProtection.DTOs;

public record BrandThreatDto(
    Guid Id,
    string LookAlikeDomain,
    string VariationType,
    bool ResolvesViaDns,
    string? ResolvedIpAddress,
    bool RespondedViaHttp,
    int? HttpStatusCode,
    string? HttpTitle,
    bool RedirectsToOriginal,
    BrandThreatRiskLevel RiskLevel,
    BrandThreatStatus Status,
    DateTime LastCheckedAt,
    DateTime? ResolvedAt,
    DateTime CreatedAt)
{
    public static BrandThreatDto From(BrandThreat t) => new(
        t.Id,
        t.LookAlikeDomain,
        t.VariationType,
        t.ResolvesViaDns,
        t.ResolvedIpAddress,
        t.RespondedViaHttp,
        t.HttpStatusCode,
        t.HttpTitle,
        t.RedirectsToOriginal,
        t.RiskLevel,
        t.Status,
        t.LastCheckedAt,
        t.ResolvedAt,
        t.CreatedAt);
}

public record BrandThreatsPagedDto(
    int TotalThreats,
    int ActiveCount,
    int ResolvedCount,
    int MonitoringCount,
    PagedResult<BrandThreatDto> Threats);