using Application.Features.Alerts.Common;
using Domain.Entities;
using Domain.Enums;
using Domain.Events;

namespace Application.Features.Alerts.ScanCompleted;

public static class ScanCompletedAlertFactory
{
    public static Alert Create(ScanCompletedEvent e, AlertChannel channel)
    {
        var severity = e.FindingSeverities.Any(s => s == FindingSeverity.Critical)
            ? AlertSeverity.Critical
            : e.FindingSeverities.Any(s => s == FindingSeverity.High)
                ? AlertSeverity.Warning
                : AlertSeverity.Info;

        return Alert.Create(
            userId: e.UserId,
            type: AlertType.ScanCompleted,
            channel: channel,
            severity: severity,
            deduplicationKey: e.ScanId.ToString(),
            subject: BuildSubject(e),
            body: BuildBody(e),
            domainId: e.DomainId);
    }

    private static string BuildSubject(ScanCompletedEvent e)
    {
        var critical = e.FindingSeverities.Count(s => s == FindingSeverity.Critical);
        var high = e.FindingSeverities.Count(s => s == FindingSeverity.High);

        if (critical > 0)
            return $"{e.DomainName} — {critical} critical finding{(critical > 1 ? "s" : "")} detected";

        if (high > 0)
            return $"{e.DomainName} — scan completed with {high} high severity finding{(high > 1 ? "s" : "")}";

        return $"{e.DomainName} — scan completed, no critical issues";
    }

    private static string BuildBody(ScanCompletedEvent e)
    {
        var critical = e.FindingSeverities.Count(s => s == FindingSeverity.Critical);
        var high     = e.FindingSeverities.Count(s => s == FindingSeverity.High);
        var medium   = e.FindingSeverities.Count(s => s == FindingSeverity.Medium);
        var low      = e.FindingSeverities.Count(s => s == FindingSeverity.Low);
        var total    = e.FindingSeverities.Count;

        var riskLabel = e.SecurityScore switch
        {
            >= 80 => "Low Risk",
            >= 60 => "Moderate Risk",
            _     => "High Risk"
        };

        var scoreColor = e.SecurityScore switch
        {
            >= 80 => "#16a34a",
            >= 60 => "#d97706",
            _     => "#dc2626"
        };

        var hasCriticalOrHigh = critical > 0 || high > 0;
        var bannerBg    = hasCriticalOrHigh ? "#FEF2F2" : "#F0FDF4";
        var bannerBorder = hasCriticalOrHigh ? "#FECACA" : "#BBF7D0";
        var bannerText  = hasCriticalOrHigh ? "#991B1B" : "#166534";
        var bannerLabel = hasCriticalOrHigh
            ? (critical > 0 ? "Action Required — Critical Issues Found" : "Review Recommended — High Severity Issues Found")
            : "All Clear — No Critical Issues Detected";

        var findingRowsHtml = BuildFindingRows(critical, high, medium, low);
        var recommendationHtml = BuildRecommendationSection(critical, high, e.DomainName);

        return AlertEmailTemplates.Wrap(
            title: "Scan Completed",
            previewText: $"{e.DomainName} scan score: {e.SecurityScore}/100 — {total} finding{(total == 1 ? "" : "s")}",
            innerContent: $"""
                <!-- Severity Banner -->
                <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 24px;">
                  <tr>
                    <td style="background-color:{bannerBg};border:1px solid {bannerBorder};padding:12px 20px;border-radius:6px;">
                      <span style="font-size:13px;font-weight:600;color:{bannerText};">{bannerLabel}</span>
                    </td>
                  </tr>
                </table>

                <h1 style="margin:0 0 8px;font-size:22px;font-weight:600;color:#0f172a;">
                  Scan Report — {e.DomainName}
                </h1>
                <p style="margin:0 0 28px;font-size:15px;color:#52525b;line-height:1.6;">
                  Your scheduled security scan has completed. Here's a summary of what was found.
                </p>

                <!-- Score + Risk Row -->
                <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 28px;border:1px solid #e2e8f0;border-radius:8px;overflow:hidden;">
                  <tr>
                    <td style="padding:16px 20px;border-bottom:1px solid #e2e8f0;background:#f8fafc;" width="50%">
                      <span style="font-size:12px;color:#94a3b8;display:block;margin-bottom:4px;">Security Score</span>
                      <span style="font-size:28px;font-weight:700;color:{scoreColor};">{e.SecurityScore}</span>
                      <span style="font-size:14px;color:{scoreColor};font-weight:500;">/100</span>
                    </td>
                    <td style="padding:16px 20px;border-bottom:1px solid #e2e8f0;background:#f8fafc;" width="50%">
                      <span style="font-size:12px;color:#94a3b8;display:block;margin-bottom:4px;">Risk Level</span>
                      <span style="font-size:18px;font-weight:600;color:{scoreColor};">{riskLabel}</span>
                    </td>
                  </tr>
                  <tr>
                    <td colspan="2" style="padding:16px 20px;background:#f8fafc;">
                      <span style="font-size:12px;color:#94a3b8;display:block;margin-bottom:4px;">Domain</span>
                      <span style="font-size:15px;font-weight:600;color:#0f172a;">{e.DomainName}</span>
                    </td>
                  </tr>
                </table>

                <!-- Findings Breakdown -->
                <p style="margin:0 0 12px;font-size:14px;font-weight:600;color:#0f172a;">Findings Breakdown</p>
                <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 28px;border:1px solid #e2e8f0;border-radius:8px;overflow:hidden;">
                  {findingRowsHtml}
                  <tr>
                    <td style="padding:12px 20px;background:#f8fafc;border-top:2px solid #e2e8f0;">
                      <span style="font-size:13px;font-weight:600;color:#0f172a;">Total Findings</span>
                    </td>
                    <td style="padding:12px 20px;background:#f8fafc;border-top:2px solid #e2e8f0;text-align:right;">
                      <span style="font-size:13px;font-weight:700;color:#0f172a;">{total}</span>
                    </td>
                  </tr>
                </table>

                {recommendationHtml}

                <!-- CTA -->
                <table cellpadding="0" cellspacing="0" style="margin:0 0 28px;">
                  <tr>
                    <td style="background-color:#0f172a;border-radius:6px;">
                      <a href="https://app.vulnwatch.io/scans/{e.ScanId}/report"
                         style="display:inline-block;padding:12px 28px;font-size:14px;font-weight:600;color:#ffffff;text-decoration:none;">
                        View Full Report →
                      </a>
                    </td>
                  </tr>
                </table>

                <hr style="border:none;border-top:1px solid #e4e4e7;margin:0 0 20px;"/>
                <p style="margin:0;font-size:12px;color:#a1a1aa;line-height:1.6;">
                  Scan notifications are sent after every completed scan. To adjust your notification
                  preferences, visit your account settings.
                </p>
                """);
    }

    private static string BuildFindingRows(int critical, int high, int medium, int low)
    {
        static string Row(string label, int count, string dotColor, bool isFirst = false)
        {
            var border = isFirst ? "" : "border-top:1px solid #e2e8f0;";
            var countColor = count > 0 ? dotColor : "#94a3b8";
            return $"""
                <tr>
                  <td style="padding:12px 20px;background:#ffffff;{border}">
                    <span style="display:inline-block;width:8px;height:8px;border-radius:50%;
                                 background-color:{dotColor};margin-right:8px;vertical-align:middle;"></span>
                    <span style="font-size:13px;color:#374151;">{label}</span>
                  </td>
                  <td style="padding:12px 20px;background:#ffffff;{border}text-align:right;">
                    <span style="font-size:13px;font-weight:600;color:{countColor};">{count}</span>
                  </td>
                </tr>
                """;
        }

        return Row("Critical", critical, "#dc2626", isFirst: true)
             + Row("High",     high,     "#ea580c")
             + Row("Medium",   medium,   "#d97706")
             + Row("Low",      low,      "#16a34a");
    }

    private static string BuildRecommendationSection(int critical, int high, string domainName)
    {
        if (critical == 0 && high == 0)
            return """
                <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 28px;background-color:#F0FDF4;border:1px solid #BBF7D0;border-radius:8px;">
                  <tr><td style="padding:20px 24px;">
                    <p style="margin:0;font-size:13px;font-weight:700;color:#166534;">✅ Good news</p>
                    <p style="margin:8px 0 0;font-size:13px;color:#15803d;line-height:1.6;">
                      No critical or high severity issues were detected. Keep monitoring regularly
                      to stay ahead of new vulnerabilities.
                    </p>
                  </td></tr>
                </table>
                """;

        var issueCount = critical + high;
        return $"""
            <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 28px;background-color:#FEF2F2;border:1px solid #FECACA;border-radius:8px;">
              <tr><td style="padding:20px 24px;">
                <p style="margin:0 0 10px;font-size:13px;font-weight:700;color:#991B1B;">⚠️ Immediate action recommended</p>
                <ul style="margin:0;padding:0 0 0 20px;font-size:13px;color:#b91c1c;line-height:2;">
                  {(critical > 0 ? $"<li>{critical} critical issue{(critical > 1 ? "s require" : " requires")} urgent remediation on <strong>{domainName}</strong></li>" : "")}
                  {(high > 0 ? $"<li>{high} high severity issue{(high > 1 ? "s" : "")} should be addressed within 48 hours</li>" : "")}
                  <li>Open the full report to see remediation steps for each finding</li>
                </ul>
              </td></tr>
            </table>
            """;
    }
}