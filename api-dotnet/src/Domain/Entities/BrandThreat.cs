using Domain.Enums;

namespace Domain.Entities;

public class BrandThreat : EntityBase
{
    public Guid DomainId { get; set; }
    public ScannedDomain Domain { get; set; } = null!;

    public string LookAlikeDomain { get; set; } = string.Empty;   // e.g. "gooogle.com"
    public string VariationType { get; set; } = string.Empty;     // Typo, Homoglyph, AltTld, Prefix, Suffix

    // DNS probe
    public bool ResolvesViaDns { get; set; }
    public string? ResolvedIpAddress { get; set; }

    // HTTP probe
    public bool RespondedViaHttp { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? HttpTitle { get; set; }                        // <title> of the page if any
    public bool RedirectsToOriginal { get; set; }                 // might just be a redirect, not a threat

    public BrandThreatRiskLevel RiskLevel { get; set; }           // Low, Medium, High
    public BrandThreatStatus Status { get; set; }                 // Active, Resolved, Monitoring

    public DateTime LastCheckedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    private BrandThreat() { }

    public static BrandThreat Create(Guid domainId, string candidate, string variationType,
    bool resolvesViaDns, bool respondedViaHttp, bool redirectsToOriginal, BrandThreatRiskLevel riskLevel, string? resolvedIpAddress, int? httpStatusCode, string? httpTitle)
        => new()
        {
            DomainId = domainId,
            LookAlikeDomain = candidate,
            VariationType = variationType,
            ResolvesViaDns = resolvesViaDns,
            ResolvedIpAddress = resolvedIpAddress,
            RespondedViaHttp = respondedViaHttp,
            HttpStatusCode = httpStatusCode,
            HttpTitle = httpTitle,
            RedirectsToOriginal = redirectsToOriginal,
            RiskLevel = riskLevel,
            Status = BrandThreatStatus.Active,
            LastCheckedAt = DateTime.UtcNow,
        };
}

