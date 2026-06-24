using Application.Features.Compliance.DTOs;
using Domain.Entities;
using Domain.Enums;

namespace Application.Helpers;

public class OwaspEvaluationEngine
{
    // Maps FindingSurface/Title patterns to OWASP categories
    private static readonly Dictionary<string, List<string>> CategoryMappings = new()
    {
        ["A01"] = ["Public Admin Panel", "Exposed Dashboard", "Open Directory Listing"],
        ["A02"] = ["Expired SSL", "Weak TLS", "Weak Cipher", "Self-Signed Certificate"],
        ["A05"] = ["Missing CSP Header", "Missing HSTS", "Missing X-Frame-Options", "Missing X-Content-Type-Options"],
        ["A06"] = ["Outdated Framework", "Deprecated Component"],
    };

    private static readonly Dictionary<string, string> CategoryNames = new()
    {
        ["A01"] = "Broken Access Control",
        ["A02"] = "Cryptographic Failures",
        ["A03"] = "Injection",
        ["A04"] = "Insecure Design",
        ["A05"] = "Security Misconfiguration",
        ["A06"] = "Vulnerable and Outdated Components",
        ["A07"] = "Identification and Authentication Failures",
        ["A08"] = "Software and Data Integrity Failures",
        ["A09"] = "Security Logging and Monitoring Failures",
        ["A10"] = "Server-Side Request Forgery",
    };

    public OwaspEvaluationResult Evaluate(ICollection<Finding> findings)
    {
        var enriched = findings
            .Where(f => f.Status == FindingStatus.Open)
            .Where(f => !f.Title.Contains("completed", StringComparison.OrdinalIgnoreCase))
            .Select(EnrichedFinding.From)
            .ToList();

        var categoryResults = CategoryMappings.Select(kvp =>
        {
            var code = kvp.Key;
            var keywords = kvp.Value;

            var matched = enriched
                .Where(e => keywords.Any(k =>
                    e.Raw.Title.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // Extract specific details from payloads for this category
            var technicalDetails = ExtractTechnicalDetails(code, matched);

            var score = CalculateScore(matched.Select(e => e.Raw).ToList());
            var status = DetermineStatus(matched.Select(e => e.Raw).ToList());

            return new OwaspCategoryResult(
                code, CategoryNames[code], score, status,
                matched.Select(e => e.Raw).ToList(),
                technicalDetails); // ← new field
        }).ToList();

        var overall = categoryResults.Any()
            ? (int)categoryResults.Average(c => c.Score) : 100;

        var tier = overall switch
        {
            >= 90 => "Excellent",
            >= 75 => "Good",
            >= 50 => "Needs Attention",
            _ => "High Risk"
        };

        return new OwaspEvaluationResult(overall, tier, categoryResults);
    }

    private List<string> ExtractTechnicalDetails(string categoryCode, List<EnrichedFinding> findings)
    {
        var details = new List<string>();

        foreach (var finding in findings)
        {
            if (finding.HttpHeaders is { } h)
            {
                if (h.MissingHeaders?.Any() == true)
                    details.Add($"Missing headers: {string.Join(", ", h.MissingHeaders)}");
                if (h.ExposedTechnology != null)
                    details.Add($"Exposed technology: {h.ExposedTechnology}");
                if (h.ServerHeader != null)
                    details.Add($"Server header exposed: {h.ServerHeader}");
            }

            if (finding.Ssl is { } ssl)
            {
                if (ssl.IsExpired)
                    details.Add("Certificate is expired");
                else if (ssl.DaysUntilExpiry < 30)
                    details.Add($"Certificate expires in {ssl.DaysUntilExpiry} days ({ssl.CertExpiry:yyyy-MM-dd})");
                if (ssl.IsSelfSigned)
                    details.Add("Certificate is self-signed");
                if (ssl.Protocol is "TLSv1.0" or "TLSv1.1")
                    details.Add($"Weak TLS protocol in use: {ssl.Protocol}");
                details.Add($"Cipher suite: {ssl.CipherSuite}");
            }

            if (finding.Dns is { } dns)
            {
                if (!dns.HasSPF) details.Add("SPF record missing");
                if (!dns.HasDMARC) details.Add("DMARC record missing");
                if (!dns.HasMX) details.Add("MX record missing");
            }
        }

        return details.Distinct().ToList();
    }

    private List<Finding> MapFindingsToCategory(string code, List<Finding> findings)
    {
        if (!CategoryMappings.TryGetValue(code, out var keywords))
            return [];

        return findings
            .Where(f => keywords.Any(k =>
                f.Title.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                (f.AiExplanation?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false)))
            .ToList();
    }


    private static int CalculateScore(List<Finding> findings)
    {
        if (!findings.Any()) return 100;

        var penalty = findings.Sum(f => f.Severity switch
        {
            FindingSeverity.Critical => 40,
            FindingSeverity.High => 25,
            FindingSeverity.Medium => 15,
            FindingSeverity.Low => 5,
            _ => 0
        });

        return Math.Max(0, 100 - penalty);
    }

    private static string DetermineStatus(List<Finding> findings)
    {
        if (!findings.Any())
            return "Compliant";

        if (findings.All(f => f.Severity == FindingSeverity.Low))
            return "PartiallyCompliant";

        return "NonCompliant";
    }
}