using Application.Features.Alerts.Common;
using Domain.Entities;
using Domain.Enums;
using Domain.Events;

namespace Application.Features.Alerts.BreachMonitoring;

public static class CredentialBreachAlertFactory
{
    public static Alert Create(CredentialBreachEvent e, AlertChannel channel, string appBaseUrl)
    {
        return Alert.Create(
            userId: e.Domain.UserId,
            type: AlertType.CredentialBreach,
            channel: channel,
            severity: AlertSeverity.Critical,
            deduplicationKey: $"breach-{e.Email.Id}-{e.Email.BreachCount}",
            subject: BuildSubject(e),
            body: BuildBody(e, appBaseUrl),
            domainId: e.Domain.Id);
    }

    private static string BuildSubject(CredentialBreachEvent e)
    {
        var count = e.Email.BreachCount;
        return $"{e.Domain.DomainName} — credential breach detected for {e.Email.EmailAddress} " +
               $"({count} breach{(count > 1 ? "es" : "")})";
    }

    private static string BuildBody(CredentialBreachEvent e, string appBaseUrl)
    {
        var email       = e.Email;
        var breachCount = email.BreachCount;
        var breachList  = e.BreachNames.Any()
            ? string.Join("", e.BreachNames.Take(10).Select(b =>
                $"<li style='line-height:2;font-size:13px;color:#b91c1c;'>{b}</li>"))
            : "<li style='line-height:2;font-size:13px;color:#b91c1c;'>Unknown sources</li>";

        var moreBreaches = e.BreachNames.Count > 10
            ? $"<p style='margin:8px 0 0;font-size:12px;color:#94a3b8;'>...and {e.BreachNames.Count - 10} more</p>"
            : "";
        
         var ctaUrl = $"{appBaseUrl}/trust-compliance?domainId={e.Domain.Id}";


        return AlertEmailTemplates.Wrap(
            title: "Credential Breach Detected",
            previewText: $"{email.EmailAddress} found in {breachCount} data breach{(breachCount > 1 ? "es" : "")}",
            innerContent: $"""
                <!-- Critical Banner -->
                <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 24px;">
                  <tr>
                    <td style="background-color:#FEF2F2;border:1px solid #FECACA;
                                padding:12px 20px;border-radius:6px;">
                      <span style="font-size:13px;font-weight:600;color:#991B1B;">
                        Credential Breach Detected — Immediate Action Required
                      </span>
                    </td>
                  </tr>
                </table>

                <h1 style="margin:0 0 8px;font-size:22px;font-weight:600;color:#0f172a;">
                  Email Found in Data Breach
                </h1>
                <p style="margin:0 0 28px;font-size:15px;color:#52525b;line-height:1.6;">
                  An email address associated with <strong>{e.Domain.DomainName}</strong> has been
                  found in one or more known data breaches. Credentials may be compromised.
                </p>

                <!-- Breach Detail Card -->
                <table cellpadding="0" cellspacing="0" width="100%"
                       style="margin:0 0 24px;border:1px solid #e2e8f0;border-radius:8px;overflow:hidden;">
                  <tr>
                    <td style="padding:10px 20px;background:#f8fafc;font-size:13px;color:#374151;" width="35%">
                      Email Address
                    </td>
                    <td style="padding:10px 20px;background:#f8fafc;font-size:14px;
                                font-weight:700;color:#dc2626;">
                      {email.EmailAddress}
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:13px;color:#374151;">
                      Domain
                    </td>
                    <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:13px;
                                color:#0f172a;font-weight:500;">
                      {e.Domain.DomainName}
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:13px;color:#374151;">
                      Breaches Found
                    </td>
                    <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:14px;
                                font-weight:700;color:#dc2626;">
                      {breachCount}
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:13px;color:#374151;">
                      Detected At
                    </td>
                    <td style="padding:10px 20px;border-top:1px solid #e2e8f0;font-size:13px;color:#0f172a;">
                      {email.LatestDetectionAt:MMMM dd, yyyy HH:mm} UTC
                    </td>
                  </tr>
                </table>

                <!-- Breach Sources -->
                <p style="margin:0 0 12px;font-size:14px;font-weight:600;color:#0f172a;">
                  Found in the following breaches:
                </p>
                <table cellpadding="0" cellspacing="0" width="100%"
                       style="margin:0 0 24px;background:#FEF2F2;border:1px solid #FECACA;border-radius:8px;">
                  <tr><td style="padding:16px 24px;">
                    <ul style="margin:0;padding:0 0 0 20px;">
                      {breachList}
                    </ul>
                    {moreBreaches}
                  </td></tr>
                </table>

                <!-- What to do -->
                <table cellpadding="0" cellspacing="0" width="100%"
                       style="margin:0 0 28px;background-color:#FFFBEB;border:1px solid #FDE68A;border-radius:8px;">
                  <tr><td style="padding:20px 24px;">
                    <p style="margin:0 0 10px;font-size:13px;font-weight:700;color:#92400E;">
                      Recommended Actions
                    </p>
                    <ul style="margin:0;padding:0 0 0 20px;font-size:13px;color:#b45309;line-height:2;">
                      <li>Change the password for <strong>{email.EmailAddress}</strong> immediately</li>
                      <li>Enable two-factor authentication on all accounts using this email</li>
                      <li>Check for reused passwords across other services and rotate them</li>
                      <li>Monitor for unusual login activity or unauthorised account access</li>
                      <li>Consider using a password manager to maintain unique credentials</li>
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
                        View Breach Details →
                      </a>
                    </td>
                  </tr>
                </table>

                <hr style="border:none;border-top:1px solid #e4e4e7;margin:0 0 20px;"/>
                <p style="margin:0;font-size:12px;color:#a1a1aa;line-height:1.6;">
                  Breach data is provided by HaveIBeenPwned. Monitoring runs on a daily cadence.
                  To manage monitored emails, visit your domain settings.
                </p>
                """);
    }
}