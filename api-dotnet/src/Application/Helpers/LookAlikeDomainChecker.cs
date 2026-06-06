using Application.Interfaces;
using Application.Features.BrandProtection.DTOs;
using Domain.Enums;
using System.Net;
using System.Text.RegularExpressions;

namespace Application.Helpers;
public record LookAlikeCheckResult(
    string Candidate,
    bool ResolvesViaDns,
    string? ResolvedIpAddress,
    bool RespondedViaHttp,
    int? HttpStatusCode,
    string? HttpTitle,
    bool RedirectsToOriginal,
    BrandThreatRiskLevel RiskLevel);

public class LookAlikeDomainChecker(IHttpClientFactory httpClientFactory)
{
    public async Task<LookAlikeCheckResult> CheckAsync(
        string candidate, string originalDomain, CancellationToken ct)
    {
        var dnsResult  = await ProbeDnsAsync(candidate, ct);
        var httpResult = dnsResult.Resolves
            ? await ProbeHttpAsync(candidate, originalDomain, ct)
            : HttpProbeResult.Empty;

        var risk = DetermineRisk(dnsResult, httpResult);

        return new LookAlikeCheckResult(
            candidate,
            dnsResult.Resolves,
            dnsResult.IpAddress,
            httpResult.Responded,
            httpResult.StatusCode,
            httpResult.Title,
            httpResult.RedirectsToOriginal,
            risk);
    }

    private async Task<DnsProbeResult> ProbeDnsAsync(string candidate, CancellationToken ct)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(candidate, ct);
            var ip = addresses.FirstOrDefault()?.ToString();
            return new DnsProbeResult(ip != null, ip);
        }
        catch
        {
            return new DnsProbeResult(false, null);
        }
    }

    private async Task<HttpProbeResult> ProbeHttpAsync(
        string candidate, string originalDomain, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("BrandProtection");
            var response = await client.GetAsync($"https://{candidate}", ct);

            var body = await response.Content.ReadAsStringAsync(ct);
            var title = ExtractTitle(body);

            // Check if it just redirects back to the original
            var finalUrl = response.RequestMessage?.RequestUri?.Host ?? string.Empty;
            var redirectsToOriginal = finalUrl.Contains(originalDomain,
                StringComparison.OrdinalIgnoreCase);

            return new HttpProbeResult(
                true,
                (int)response.StatusCode,
                title,
                redirectsToOriginal);
        }
        catch
        {
            return HttpProbeResult.Empty;
        }
    }

    private static string? ExtractTitle(string html)
    {
        var match = Regex.Match(html,
            @"<title[^>]*>(.*?)</title>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static BrandThreatRiskLevel DetermineRisk(
        DnsProbeResult dns, HttpProbeResult http)
    {
        // Doesn't even resolve → Low (still worth tracking)
        if (!dns.Resolves) return BrandThreatRiskLevel.Low;

        // Resolves but just redirects to the real domain → Low
        if (http.RedirectsToOriginal) return BrandThreatRiskLevel.Low;

        // Resolves and serves content → High
        if (http.Responded && http.StatusCode is >= 200 and < 400)
            return BrandThreatRiskLevel.High;

        // Resolves but HTTP failed/errored → Medium (parked/misconfigured)
        return BrandThreatRiskLevel.Medium;
    }

    private record DnsProbeResult(bool Resolves, string? IpAddress);

    private record HttpProbeResult(
        bool Responded, int? StatusCode, string? Title, bool RedirectsToOriginal)
    {
        public static HttpProbeResult Empty => new(false, null, null, false);
    }
}