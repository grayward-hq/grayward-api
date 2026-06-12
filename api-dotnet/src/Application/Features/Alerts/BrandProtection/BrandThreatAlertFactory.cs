using Application.Features.Alerts.Common;
using Domain.Entities;
using Domain.Enums;
using Domain.Events;

namespace Application.Features.Alerts.BrandProtection;

public static class BrandThreatAlertFactory
{
    public static Alert Create(BrandThreatDetectedEvent e, AlertChannel channel, string appBaseUrl)
    {
        var severity = e.Threat.RiskLevel switch
        {
            BrandThreatRiskLevel.High   => AlertSeverity.Critical,
            BrandThreatRiskLevel.Medium => AlertSeverity.Warning,
            _                           => AlertSeverity.Info
        };

        return Alert.Create(
            userId: e.Domain.UserId,
            type: AlertType.BrandThreat,
            channel: channel,
            severity: severity,
            deduplicationKey: $"brand-threat-{e.Threat.Id}",
            subject: BuildSubject(e),
            body: BuildBody(e, appBaseUrl),
            domainId: e.Domain.Id,
            summary: BuildSummary(e, appBaseUrl));
    }

    private static string BuildSubject(BrandThreatDetectedEvent e) =>
        e.Threat.RiskLevel switch
        {
            BrandThreatRiskLevel.High =>
                $"{e.Domain.DomainName} — high-risk lookalike domain detected: {e.Threat.LookAlikeDomain}",
            BrandThreatRiskLevel.Medium =>
                $"{e.Domain.DomainName} — suspicious lookalike domain found: {e.Threat.LookAlikeDomain}",
            _ =>
                $"{e.Domain.DomainName} — lookalike domain registered: {e.Threat.LookAlikeDomain}"
        };

    private static string BuildBody(BrandThreatDetectedEvent e, string appBaseUrl)
    {
        var threat = e.Threat;

        var riskColor = threat.RiskLevel switch
        {
            BrandThreatRiskLevel.High   => "#dc2626",
            BrandThreatRiskLevel.Medium => "#d97706",
            _                           => "#2563eb"
        };

        var bannerBg = threat.RiskLevel switch
        {
            BrandThreatRiskLevel.High   => "#FEF2F2",
            BrandThreatRiskLevel.Medium => "#FFFBEB",
            _                           => "#EFF6FF"
        };

        var bannerBorder = threat.RiskLevel switch
        {
            BrandThreatRiskLevel.High   => "#FECACA",
            BrandThreatRiskLevel.Medium => "#FDE68A",
            _                           => "#BFDBFE"
        };

        var bannerTextColor = threat.RiskLevel switch
        {
            BrandThreatRiskLevel.High   => "#991B1B",
            BrandThreatRiskLevel.Medium => "#92400E",
            _                           => "#1E40AF"
        };

        var bannerLabel = threat.RiskLevel switch
        {
            BrandThreatRiskLevel.High   => "⚠️ High Risk — Active Lookalike Domain Detected",
            BrandThreatRiskLevel.Medium => "⚠️ Medium Risk — Suspicious Domain Registered",
            _                           => "ℹ️ Low Risk — Lookalike Domain Found"
        };

        var dnsRow = threat.ResolvesViaDns
            ? $"<span style='color:#16a34a;font-weight:600;'>✓ Resolves</span> ({threat.ResolvedIpAddress ?? "unknown IP"})"
            : "<span style='color:#94a3b8;'>✗ Does not resolve</span>";

        var httpRow = threat.RespondedViaHttp
            ? $"<span style='color:#dc2626;font-weight:600;'>✓ Serving content</span> (HTTP {threat.HttpStatusCode})"
            : "<span style='color:#94a3b8;'>✗ No HTTP response</span>";

        var titleRow = !string.IsNullOrWhiteSpace(threat.HttpTitle)
            ? $"""
              <tr>
                <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:13px;color:#374151;">Page Title</td>
                <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:13px;color:#0f172a;font-weight:500;">
                  {threat.HttpTitle}
                </td>
              </tr>
              """
            : "";

        var redirectNote = threat.RedirectsToOriginal
            ? """
              <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 24px;background:#F0FDF4;border:1px solid #BBF7D0;border-radius:8px;">
                <tr><td style="padding:14px 20px;font-size:13px;color:#166534;">
                  ℹ️ This domain currently redirects to your site. It may be harmless, but monitor it closely
                  as redirect behaviour can change.
                </td></tr>
              </table>
              """
            : "";

        var ctaUrl = $"{appBaseUrl}/trust-compliance?domainId={e.Domain.Id}";
        

        return AlertEmailTemplates.Wrap(
            title: "Brand Threat Detected",
            previewText: $"Lookalike domain {threat.LookAlikeDomain} detected for {e.Domain.DomainName}",
            innerContent: $"""
                <!-- Risk Banner -->
                <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 24px;">
                  <tr>
                    <td style="background-color:{bannerBg};border:1px solid {bannerBorder};
                                padding:12px 20px;border-radius:6px;">
                      <span style="font-size:13px;font-weight:600;color:{bannerTextColor};">
                        {bannerLabel}
                      </span>
                    </td>
                  </tr>
                </table>

                <h1 style="margin:0 0 8px;font-size:22px;font-weight:600;color:#0f172a;">
                  Lookalike Domain Detected
                </h1>
                <p style="margin:0 0 28px;font-size:15px;color:#52525b;line-height:1.6;">
                  A domain similar to <strong>{e.Domain.DomainName}</strong> has been detected.
                  This may indicate a phishing attempt, brand impersonation, or typosquatting attack.
                </p>

                <!-- Threat Detail Card -->
                <table cellpadding="0" cellspacing="0" width="100%"
                       style="margin:0 0 24px;border:1px solid #e2e8f0;border-radius:8px;overflow:hidden;">
                  <tr>
                    <td style="padding:10px 20px;background:#f8fafc;font-size:13px;color:#374151;" width="35%">
                      Lookalike Domain
                    </td>
                    <td style="padding:10px 20px;background:#f8fafc;font-size:14px;
                                font-weight:700;color:{riskColor};">
                      {threat.LookAlikeDomain}
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:13px;color:#374151;">
                      Your Domain
                    </td>
                    <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:13px;
                                color:#0f172a;font-weight:500;">
                      {e.Domain.DomainName}
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:13px;color:#374151;">
                      Variation Type
                    </td>
                    <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:13px;color:#0f172a;">
                      {threat.VariationType}
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:13px;color:#374151;">
                      Risk Level
                    </td>
                    <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:13px;
                                font-weight:600;color:{riskColor};">
                      {threat.RiskLevel}
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:13px;color:#374151;">
                      DNS
                    </td>
                    <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:13px;">
                      {dnsRow}
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:13px;color:#374151;">
                      HTTP
                    </td>
                    <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:13px;">
                      {httpRow}
                    </td>
                  </tr>
                  {titleRow}
                </table>

                {redirectNote}

                <!-- What to do -->
                <table cellpadding="0" cellspacing="0" width="100%"
                       style="margin:0 0 28px;background-color:#FFFBEB;border:1px solid #FDE68A;border-radius:8px;">
                  <tr><td style="padding:20px 24px;">
                    <p style="margin:0 0 10px;font-size:13px;font-weight:700;color:#92400E;">
                      What should you do?
                    </p>
                    <ul style="margin:0;padding:0 0 0 20px;font-size:13px;color:#b45309;line-height:2;">
                      <li>Check if this domain is owned by you or a partner — if so, dismiss it in VulnWatch</li>
                      <li>If unknown, investigate the registrant via WHOIS lookup</li>
                      <li>If serving malicious content, report to the registrar and relevant abuse contacts</li>
                      <li>Consider registering common variations of your domain proactively</li>
                    </ul>
                  </td></tr>
                </table>

                <!-- CTA -->
                <table cellpadding="0" cellspacing="0" style="margin:0 0 28px;">
                  <tr>
                    <td style="background-color:#0f172a;border-radius:6px;">
                      <a href="{ctaUrl}"
                         style="display:inline-block;padding:12px 28px;font-size:14px;
                                font-weight:600;color:#ffffff;text-decoration:none;">
                        View Brand Threats →
                      </a>
                    </td>
                  </tr>
                </table>

                <hr style="border:none;border-top:1px solid #e4e4e7;margin:0 0 20px;"/>
                <p style="margin:0;font-size:12px;color:#a1a1aa;line-height:1.6;">
                  Brand protection monitoring runs on every scheduled scan cycle.
                  To dismiss false positives or adjust settings, visit your domain settings.
                </p>
                """);
    }

  private static string BuildSummary(BrandThreatDetectedEvent e, string appBaseUrl)
  {
      var ctaUrl = $"{appBaseUrl}/trust-compliance?domainId={e.Domain.Id}";
      var threat = e.Threat;

      var dns  = threat.ResolvesViaDns ? $"resolves ({threat.ResolvedIpAddress ?? "unknown IP"})" : "does not resolve";
      var http = threat.RespondedViaHttp ? $"serving content (HTTP {threat.HttpStatusCode})" : "no HTTP response";

      return string.Join("\n",
          $"Lookalike domain: *{threat.LookAlikeDomain}*",
          $"Risk level: *{threat.RiskLevel}*",
          $"DNS: {dns} | HTTP: {http}",
          $"<{ctaUrl}|View brand threats>");
  }
}