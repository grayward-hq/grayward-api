using Application.Features.Alerts.Common;
using Domain.Entities;
using Domain.Enums;
using Domain.Events;

namespace Application.Features.Alerts.SslExpiry;

// ── Event ─────────────────────────────────────────────────────────────────────
// (keep in Domain/Events/SslExpiryEvent.cs — domain events belong in Domain)

// ── Factory ───────────────────────────────────────────────────────────────────
public static class SslExpiryAlertFactory
{
  public static Alert Create(SslExpiryEvent e, AlertChannel channel)
  {
    var severity = e.DaysRemaining <= 7
        ? AlertSeverity.Critical
        : AlertSeverity.Warning;

    return Alert.Create(
        userId: e.UserId,
        type: AlertType.SslExpiry,
        channel: channel,
        severity: severity,
        deduplicationKey: DateTime.UtcNow.ToString("yyyy-MM-dd"),
        subject: $"SSL certificate for {e.DomainName} expires in {e.DaysRemaining} days",
        body: BuildBody(e),
        domainId: e.DomainId);
  }

  private static string BuildBody(SslExpiryEvent e)
  {
    var isCritical = e.DaysRemaining <= 7;
    var severityLabel = isCritical ? "Critical — Immediate Action Required" : "Warning — Action Required Soon";
    var accentColor = isCritical ? "#3B6D11" : "#639922";

    return AlertEmailTemplates.Wrap(
        title: "SSL Certificate Expiry Warning",
        previewText: $"{e.DomainName} SSL expires in {e.DaysRemaining} days",
        innerContent: $"""
            <!-- Severity Banner -->
            <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 24px;">
              <tr>
                <td style="background-color:#EAF3DE;border:1px solid #97C459;padding:12px 20px;border-radius:6px;">
                  <span style="font-size:13px;font-weight:600;color:#27500A;">{severityLabel}</span>
                </td>
              </tr>
            </table>

            <h1 style="margin:0 0 8px;font-size:22px;font-weight:600;color:#0f172a;">SSL Certificate Expiring Soon</h1>
            <p style="margin:0 0 28px;font-size:15px;color:#52525b;line-height:1.6;">
              Your certificate for <strong style="color:#0f172a;">{e.DomainName}</strong> requires immediate renewal to maintain secure connections.
            </p>

            <!-- Info Card -->
            <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 28px;border:1px solid #e2e8f0;border-radius:8px;overflow:hidden;">
              <tr><td style="padding:12px 20px;border-bottom:1px solid #e2e8f0;background:#f8fafc;">
                <span style="font-size:12px;color:#94a3b8;display:block;">Domain</span>
                <span style="font-size:15px;font-weight:600;color:#0f172a;">{e.DomainName}</span>
              </td></tr>
              <tr><td style="padding:12px 20px;border-bottom:1px solid #e2e8f0;background:#f8fafc;">
                <span style="font-size:12px;color:#94a3b8;display:block;">Expiry Date</span>
                <span style="font-size:15px;font-weight:600;color:{accentColor};">{e.ExpiresAt:dddd, MMMM dd, yyyy} at {e.ExpiresAt:HH:mm} UTC</span>
              </td></tr>
              <tr><td style="padding:12px 20px;background:#f8fafc;">
                <span style="font-size:12px;color:#94a3b8;display:block;">Time Remaining</span>
                <span style="font-size:15px;font-weight:600;color:{accentColor};">{e.DaysRemaining} days</span>
              </td></tr>
            </table>

            <!-- What happens -->
            <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 28px;background-color:#EAF3DE;border:1px solid #C0DD97;border-radius:8px;">
              <tr><td style="padding:20px 24px;">
                <p style="margin:0 0 10px;font-size:13px;font-weight:700;color:#27500A;">What happens if this expires?</p>
                <ul style="margin:0;padding:0 0 0 20px;font-size:13px;color:#3B6D11;line-height:2;">
                  <li>Visitors will see a "Your connection is not private" browser warning</li>
                  <li>All HTTPS traffic to <strong>{e.DomainName}</strong> will be blocked or flagged as insecure</li>
                  <li>Search engines may demote your site in rankings</li>
                  <li>APIs calling your domain will throw SSL handshake errors</li>
                  <li>User data could be exposed to interception</li>
                </ul>
              </td></tr>
            </table>

            <!-- CTA -->
            <table cellpadding="0" cellspacing="0" style="margin:0 0 28px;">
              <tr>
                <td style="background-color:#27500A;border-radius:6px;">
                  <a href="https://letsencrypt.org/docs/renewal/"
                     style="display:inline-block;padding:12px 28px;font-size:14px;font-weight:600;color:#EAF3DE;text-decoration:none;">
                    View renewal instructions
                  </a>
                </td>
              </tr>
            </table>

            <!-- Quick options -->
            <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 28px;background-color:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;">
              <tr><td style="padding:20px 24px;">
                <p style="margin:0 0 12px;font-size:13px;font-weight:700;color:#0f172a;">Quick renewal options</p>
                <table cellpadding="0" cellspacing="0" width="100%" style="font-size:13px;line-height:2;">
                  <tr>
                    <td style="width:80px;"><span style="background:#EAF3DE;color:#27500A;padding:2px 8px;border-radius:4px;font-size:12px;font-weight:600;">Certbot</span></td>
                    <td style="color:#52525b;">Run <code style="background:#f1f5f9;padding:1px 5px;border-radius:3px;font-size:12px;">certbot renew</code></td>
                  </tr>
                  <tr>
                    <td><span style="background:#EAF3DE;color:#27500A;padding:2px 8px;border-radius:4px;font-size:12px;font-weight:600;">cPanel</span></td>
                    <td style="color:#52525b;">Navigate to SSL/TLS and click renew</td>
                  </tr>
                  <tr>
                    <td><span style="background:#EAF3DE;color:#27500A;padding:2px 8px;border-radius:4px;font-size:12px;font-weight:600;">Cloudflare</span></td>
                    <td style="color:#52525b;">Enable Universal SSL in your dashboard</td>
                  </tr>
                  <tr>
                    <td><span style="background:#EAF3DE;color:#27500A;padding:2px 8px;border-radius:4px;font-size:12px;font-weight:600;">AWS ACM</span></td>
                    <td style="color:#52525b;">Auto-renews if DNS validation is configured</td>
                  </tr>
                </table>
              </td></tr>
            </table>

            <hr style="border:none;border-top:1px solid #e4e4e7;margin:0 0 20px;"/>
            <p style="margin:0;font-size:12px;color:#a1a1aa;line-height:1.6;">
              Alerts are sent at 30, 14, and 7 days before expiry. To manage notification preferences, visit your account settings.
            </p>
            """);
  }
}