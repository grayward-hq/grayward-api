using Domain.Entities;
using Domain.Enums;

namespace Application.Features.BrandProtection.DTOs;

public record BrandThreatDetailDto(
    Guid Id,
    Guid DomainId,
    string OriginalDomain,
    string LookAlikeDomain,
    string VariationType,
    BrandThreatRiskLevel RiskLevel,
    BrandThreatStatus Status,

    // DNS
    bool ResolvesViaDns,
    string? ResolvedIpAddress,

    // HTTP
    bool RespondedViaHttp,
    int? HttpStatusCode,
    string? HttpTitle,
    bool RedirectsToOriginal,

    // Timestamps
    DateTime LastCheckedAt,
    DateTime? ResolvedAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt)
{
    public static BrandThreatDetailDto From(BrandThreat t, string originalDomain) => new(
        t.Id,
        t.DomainId,
        originalDomain,
        t.LookAlikeDomain,
        t.VariationType,
        t.RiskLevel,
        t.Status,
        t.ResolvesViaDns,
        t.ResolvedIpAddress,
        t.RespondedViaHttp,
        t.HttpStatusCode,
        t.HttpTitle,
        t.RedirectsToOriginal,
        t.LastCheckedAt,
        t.ResolvedAt,
        t.CreatedAt,
        t.UpdatedAt);
}