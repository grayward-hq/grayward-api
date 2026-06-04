using Domain.Entities;
using Domain.Enums;
using System.Text;

namespace Application.Features.Chat.Helpers;

public static class ScanReportPromptBuilder
{
    public static string Build(Scan scan, ScannedDomain? domain)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are a cybersecurity assistant helping a user understand their domain security scan report.");
        sb.AppendLine("Be concise, clear, and actionable. Use plain English, avoid unnecessary jargon.");
        sb.AppendLine("If asked about something not in the report, say so honestly.");
        sb.AppendLine();
        sb.AppendLine("## SCAN REPORT CONTEXT");
        sb.AppendLine($"Domain: {domain?.DomainName ?? "unknown"}");
        sb.AppendLine($"Security Score: {scan.SecurityScore}/100");
        sb.AppendLine($"Scan completed: {scan.CompletedAt:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine("## FINDINGS");

        var openFindings = scan.Findings
            .Where(f => f.Status == FindingStatus.Open)
            .OrderBy(f => f.Severity)
            .ToList();

        if (!openFindings.Any())
        {
            sb.AppendLine("No open findings — all checks passed.");
        }
        else
        {
            foreach (var f in openFindings)
            {
                sb.AppendLine($"- [{f.Severity}] {f.Surface}: {f.Title}");

                if (!string.IsNullOrWhiteSpace(f.AiExplanation))
                    sb.AppendLine($"  Explanation: {f.AiExplanation}");

                if (!string.IsNullOrWhiteSpace(f.RemediationSteps))
                {
                    var firstStep = f.RemediationSteps
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault();
                    if (firstStep is not null)
                        sb.AppendLine($"  Fix: {firstStep}");
                }
            }
        }

        return sb.ToString();
    }
}