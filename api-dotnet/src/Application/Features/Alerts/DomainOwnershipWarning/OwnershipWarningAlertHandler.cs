using Application.Features.Alerts.Common;
using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.Events;
using Microsoft.Extensions.Configuration;

namespace Application.Features.Alerts.DomainOwnershipWarning;

public static class DomainOwnershipWarningAlertFactory
{
    public static Alert Create(DomainOwnershipWarningEvent e, AlertChannel channel, IConfiguration config)
    {
        var severity = e.Stage == OwnershipWarningStage.Revoked
                                          ? AlertSeverity.Critical
                                          : AlertSeverity.Warning;

        return Alert.Create(
                   userId: e.UserId,
                   type: AlertType.DomainStatusChanged,
                   channel: channel,
                   severity: severity,
                   deduplicationKey: $"ownership-{e.Stage}",
                   subject: BuildSubject(e),
                   body: BuildBody(e, config),
                   domainId: e.DomainId);
    }

    private static string BuildSubject(DomainOwnershipWarningEvent e) => e.Stage switch
    {
        OwnershipWarningStage.Warning =>
            $"{e.DomainName} — ownership TXT record not found",
        OwnershipWarningStage.MonitoringPaused =>
            $"{e.DomainName} — monitoring paused, TXT record still missing",
        OwnershipWarningStage.Revoked =>
            $"{e.DomainName} — domain removed due to lost ownership",
        _ => $"{e.DomainName} — ownership check failed"
    };

    private static string BuildBody(DomainOwnershipWarningEvent e, IConfiguration config)
    {
        var daysFailing = (int)Math.Floor((DateTime.UtcNow - e.FailedSince).TotalDays);
        var hoursFailing = (int)Math.Floor((DateTime.UtcNow - e.FailedSince).TotalHours);
        var failingLabel = hoursFailing < 24
            ? $"{hoursFailing} hour{(hoursFailing == 1 ? "" : "s")}"
            : $"{daysFailing} day{(daysFailing == 1 ? "" : "s")}";

        return e.Stage switch
        {
            OwnershipWarningStage.Warning => BuildWarningBody(e, failingLabel, config),
            OwnershipWarningStage.MonitoringPaused => BuildMonitoringPausedBody(e, failingLabel, config),
            OwnershipWarningStage.Revoked => BuildRevokedBody(e, failingLabel, config),
            _ => BuildWarningBody(e, failingLabel, config),
        };
    }

    private static string BuildWarningBody(
        DomainOwnershipWarningEvent e, string failingLabel, IConfiguration config)
    {
        // Warning: failing < 24h. Countdown to monitoring pause (72h from FailedSince).
        var pauseAt = e.FailedSince.AddHours(72);
        var hoursUntilPause = Math.Max(0, (int)Math.Ceiling((pauseAt - DateTime.UtcNow).TotalHours));
        var countdownLabel = hoursUntilPause <= 1
            ? "less than 1 hour"
            : $"{hoursUntilPause} hours";

        return AlertEmailTemplates.Wrap(
            title: "Domain Ownership Warning",
            previewText: $"{e.DomainName} — ownership TXT record not found ({failingLabel})",
            innerContent: $"""
            <!-- Severity Banner -->
            <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 24px;">
              <tr>
                <td style="background-color:#FFFBEB;border:1px solid #F59E0B;padding:12px 20px;border-radius:6px;">
                  <span style="font-size:13px;font-weight:600;color:#92400E;">⚠️ Warning — Action Required</span>
                </td>
              </tr>
            </table>

            <h1 style="margin:0 0 8px;font-size:22px;font-weight:600;color:#0f172a;">
              Ownership Record Not Found
            </h1>
            <p style="margin:0 0 28px;font-size:15px;color:#52525b;line-height:1.6;">
              We could not find the DNS TXT record for
              <strong style="color:#0f172a;">{e.DomainName}</strong>.
              This has been the case for <strong style="color:#92400E;">{failingLabel}</strong>.
              Monitoring is still active for now, but will be paused if the record
              is not restored within <strong style="color:#92400E;">{countdownLabel}</strong>.
            </p>

            <!-- Info Card -->
            <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 28px;border:1px solid #e2e8f0;border-radius:8px;overflow:hidden;">
              <tr><td style="padding:12px 20px;border-bottom:1px solid #e2e8f0;background:#f8fafc;">
                <span style="font-size:12px;color:#94a3b8;display:block;">Domain</span>
                <span style="font-size:15px;font-weight:600;color:#0f172a;">{e.DomainName}</span>
              </td></tr>
              <tr><td style="padding:12px 20px;border-bottom:1px solid #e2e8f0;background:#f8fafc;">
                <span style="font-size:12px;color:#94a3b8;display:block;">Failing Since</span>
                <span style="font-size:15px;font-weight:600;color:#92400E;">{e.FailedSince:dddd, MMMM dd, yyyy} at {e.FailedSince:HH:mm} UTC</span>
              </td></tr>
              <tr><td style="padding:12px 20px;border-bottom:1px solid #e2e8f0;background:#f8fafc;">
                <span style="font-size:12px;color:#94a3b8;display:block;">Duration Failing</span>
                <span style="font-size:15px;font-weight:600;color:#92400E;">{failingLabel}</span>
              </td></tr>
              <tr><td style="padding:12px 20px;background:#f8fafc;">
                <span style="font-size:12px;color:#94a3b8;display:block;">Monitoring Pauses In</span>
                <span style="font-size:15px;font-weight:600;color:#B45309;">{countdownLabel}</span>
              </td></tr>
            </table>

            <!-- Why we check this -->
            <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 28px;background-color:#EAF3DE;border:1px solid #C0DD97;border-radius:8px;">
              <tr><td style="padding:20px 24px;">
                <p style="margin:0 0 10px;font-size:13px;font-weight:700;color:#27500A;">Why do we check this?</p>
                <p style="margin:0;font-size:13px;color:#3B6D11;line-height:1.8;">
                  VulnWatch periodically confirms you still own a domain before running
                  security scans on it. This protects both you and third parties — we
                  never scan a domain without confirmed ownership. The check looks for
                  a TXT record at:
                </p>
                <p style="margin:10px 0 0;font-family:monospace;font-size:13px;color:#27500A;
                          background:#D1FAE5;padding:8px 12px;border-radius:4px;display:inline-block;">
                  _vulnwatch-verify.{e.DomainName}
                </p>
              </td></tr>
            </table>

            <!-- What to do -->
            <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 28px;background-color:#FFFBEB;border:1px solid #F59E0B;border-radius:8px;">
              <tr><td style="padding:20px 24px;">
                <p style="margin:0 0 10px;font-size:13px;font-weight:700;color:#92400E;">What should I do?</p>
                <ul style="margin:0;padding:0 0 0 20px;font-size:13px;color:#78350F;line-height:2;">
                  <li>Log in to your DNS provider and confirm the TXT record still exists at
                    <code style="background:#FEF3C7;padding:1px 5px;border-radius:3px;">_vulnwatch-verify.{e.DomainName}</code>
                  </li>
                  <li>If the record exists but this alert keeps appearing, please
                    <a href="mailto:support@vulnwatch.io" style="color:#B45309;">contact support</a>
                    — it may be a DNS propagation issue.
                  </li>
                  <li>If you removed the record intentionally and no longer need monitoring, no action is required.</li>
                </ul>
              </td></tr>
            </table>

            <hr style="border:none;border-top:1px solid #e4e4e7;margin:0 0 20px;"/>
            <p style="margin:0;font-size:12px;color:#a1a1aa;line-height:1.6;">
              Ownership checks run daily. You will receive another alert if monitoring is paused.
            </p>
            """);
    }

    private static string BuildMonitoringPausedBody(
        DomainOwnershipWarningEvent e, string failingLabel, IConfiguration config)
    {
        // MonitoringPaused: failing >= 72h. Countdown to revocation (7 days from FailedSince).
        var revokeAt = e.FailedSince.AddDays(7);
        var daysUntilRevoke = Math.Max(0, (int)Math.Ceiling((revokeAt - DateTime.UtcNow).TotalDays));
        var revokeCountdown = daysUntilRevoke <= 1 ? "less than 1 day" :
                                $"{daysUntilRevoke} day{(daysUntilRevoke == 1 ? "" : "s")}";

        return AlertEmailTemplates.Wrap(
            title: "Domain Monitoring Paused",
            previewText: $"{e.DomainName} — monitoring paused, ownership record still missing",
            innerContent: $"""
            <!-- Severity Banner -->
            <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 24px;">
              <tr>
                <td style="background-color:#FEF3C7;border:1px solid #D97706;padding:12px 20px;border-radius:6px;">
                  <span style="font-size:13px;font-weight:600;color:#78350F;">⚠️ Monitoring Paused — Immediate Action Required</span>
                </td>
              </tr>
            </table>

            <h1 style="margin:0 0 8px;font-size:22px;font-weight:600;color:#0f172a;">
              Monitoring Has Been Paused
            </h1>
            <p style="margin:0 0 28px;font-size:15px;color:#52525b;line-height:1.6;">
              We have paused security monitoring for
              <strong style="color:#0f172a;">{e.DomainName}</strong>
              because the ownership TXT record has been missing for
              <strong style="color:#B45309;">{failingLabel}</strong>.
              If the record is not restored within
              <strong style="color:#DC2626;">{revokeCountdown}</strong>,
              the domain will be removed from your account.
            </p>

            <!-- Info Card -->
            <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 28px;border:1px solid #e2e8f0;border-radius:8px;overflow:hidden;">
              <tr><td style="padding:12px 20px;border-bottom:1px solid #e2e8f0;background:#f8fafc;">
                <span style="font-size:12px;color:#94a3b8;display:block;">Domain</span>
                <span style="font-size:15px;font-weight:600;color:#0f172a;">{e.DomainName}</span>
              </td></tr>
              <tr><td style="padding:12px 20px;border-bottom:1px solid #e2e8f0;background:#f8fafc;">
                <span style="font-size:12px;color:#94a3b8;display:block;">Failing Since</span>
                <span style="font-size:15px;font-weight:600;color:#B45309;">{e.FailedSince:dddd, MMMM dd, yyyy} at {e.FailedSince:HH:mm} UTC</span>
              </td></tr>
              <tr><td style="padding:12px 20px;border-bottom:1px solid #e2e8f0;background:#f8fafc;">
                <span style="font-size:12px;color:#94a3b8;display:block;">Duration Failing</span>
                <span style="font-size:15px;font-weight:600;color:#B45309;">{failingLabel}</span>
              </td></tr>
              <tr><td style="padding:12px 20px;border-bottom:1px solid #e2e8f0;background:#f8fafc;">
                <span style="font-size:12px;color:#94a3b8;display:block;">Current Status</span>
                <span style="font-size:15px;font-weight:600;color:#B45309;">Monitoring Paused</span>
              </td></tr>
              <tr><td style="padding:12px 20px;background:#f8fafc;">
                <span style="font-size:12px;color:#94a3b8;display:block;">Domain Removed In</span>
                <span style="font-size:15px;font-weight:600;color:#DC2626;">{revokeCountdown}</span>
              </td></tr>
            </table>

            <!-- Why we check this -->
            <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 28px;background-color:#EAF3DE;border:1px solid #C0DD97;border-radius:8px;">
              <tr><td style="padding:20px 24px;">
                <p style="margin:0 0 10px;font-size:13px;font-weight:700;color:#27500A;">Why do we check this?</p>
                <p style="margin:0;font-size:13px;color:#3B6D11;line-height:1.8;">
                  VulnWatch confirms domain ownership before running security scans.
                  This protects you and third parties — we never scan a domain without
                  confirmed ownership. The check looks for a TXT record at:
                </p>
                <p style="margin:10px 0 0;font-family:monospace;font-size:13px;color:#27500A;
                          background:#D1FAE5;padding:8px 12px;border-radius:4px;display:inline-block;">
                  _vulnwatch-verify.{e.DomainName}
                </p>
              </td></tr>
            </table>

            <!-- What to do -->
            <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 28px;background-color:#FEF3C7;border:1px solid #D97706;border-radius:8px;">
              <tr><td style="padding:20px 24px;">
                <p style="margin:0 0 10px;font-size:13px;font-weight:700;color:#92400E;">How to restore monitoring</p>
                <ul style="margin:0;padding:0 0 0 20px;font-size:13px;color:#78350F;line-height:2;">
                  <li>Log in to your DNS provider and confirm the TXT record exists at
                    <code style="background:#FEF3C7;padding:1px 5px;border-radius:3px;">_vulnwatch-verify.{e.DomainName}</code>
                  </li>
                  <li>Once the record is back in DNS, return to your VulnWatch dashboard
                    and click <strong>Verify</strong> on the domain — monitoring will resume immediately.
                  </li>
                  <li>If the record exists but monitoring is still paused, please
                    <a href="mailto:support@vulnwatch.io" style="color:#B45309;">contact support</a>
                    — it may be a DNS propagation delay.
                  </li>
                </ul>
              </td></tr>
            </table>

            <hr style="border:none;border-top:1px solid #e4e4e7;margin:0 0 20px;"/>
            <p style="margin:0;font-size:12px;color:#a1a1aa;line-height:1.6;">
              No scans will run while monitoring is paused. Re-verify your domain to restore full monitoring.
            </p>
            """);
    }

    private static string BuildRevokedBody(
        DomainOwnershipWarningEvent e, string failingLabel, IConfiguration config)
    {
        var frontendBase = config["FrontendUrl:Domain"] ?? config["FrontendUrl:Verify"]!
                                .Replace("/verify", "");
        var dashboardUrl = $"{frontendBase}/domain/{e.DomainId}";

        return AlertEmailTemplates.Wrap(
            title: "Domain Removed — Ownership Lost",
            previewText: $"{e.DomainName} has been removed from your VulnWatch account",
            innerContent: $"""
            <!-- Severity Banner -->
            <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 24px;">
              <tr>
                <td style="background-color:#FEF2F2;border:1px solid #FECACA;padding:12px 20px;border-radius:6px;">
                  <span style="font-size:13px;font-weight:600;color:#991B1B;">🚨 Domain Removed — Ownership Could Not Be Confirmed</span>
                </td>
              </tr>
            </table>

            <h1 style="margin:0 0 8px;font-size:22px;font-weight:600;color:#0f172a;">
              {e.DomainName} Has Been Removed
            </h1>
            <p style="margin:0 0 28px;font-size:15px;color:#52525b;line-height:1.6;">
              The ownership TXT record for <strong style="color:#0f172a;">{e.DomainName}</strong>
              has been missing for <strong style="color:#DC2626;">{failingLabel}</strong>.
              As a result, this domain has been removed from active monitoring and
              can no longer be scanned until ownership is re-confirmed.
            </p>

            <!-- Info Card -->
            <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 28px;border:1px solid #e2e8f0;border-radius:8px;overflow:hidden;">
              <tr><td style="padding:12px 20px;border-bottom:1px solid #e2e8f0;background:#f8fafc;">
                <span style="font-size:12px;color:#94a3b8;display:block;">Domain</span>
                <span style="font-size:15px;font-weight:600;color:#0f172a;">{e.DomainName}</span>
              </td></tr>
              <tr><td style="padding:12px 20px;border-bottom:1px solid #e2e8f0;background:#f8fafc;">
                <span style="font-size:12px;color:#94a3b8;display:block;">Failing Since</span>
                <span style="font-size:15px;font-weight:600;color:#DC2626;">{e.FailedSince:dddd, MMMM dd, yyyy} at {e.FailedSince:HH:mm} UTC</span>
              </td></tr>
              <tr><td style="padding:12px 20px;border-bottom:1px solid #e2e8f0;background:#f8fafc;">
                <span style="font-size:12px;color:#94a3b8;display:block;">Duration Failing</span>
                <span style="font-size:15px;font-weight:600;color:#DC2626;">{failingLabel}</span>
              </td></tr>
              <tr><td style="padding:12px 20px;background:#f8fafc;">
                <span style="font-size:12px;color:#94a3b8;display:block;">Current Status</span>
                <span style="font-size:15px;font-weight:600;color:#DC2626;">Revoked — Monitoring Disabled</span>
              </td></tr>
            </table>

            <!-- Why we check this -->
            <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 28px;background-color:#EAF3DE;border:1px solid #C0DD97;border-radius:8px;">
              <tr><td style="padding:20px 24px;">
                <p style="margin:0 0 10px;font-size:13px;font-weight:700;color:#27500A;">Why did this happen?</p>
                <p style="margin:0;font-size:13px;color:#3B6D11;line-height:1.8;">
                  VulnWatch confirms domain ownership before running security scans
                  to ensure we only scan infrastructure you actually control. When the
                  ownership TXT record at
                  <code style="background:#D1FAE5;padding:1px 5px;border-radius:3px;">_vulnwatch-verify.{e.DomainName}</code>
                  is absent for 7 consecutive days, we remove the domain from monitoring
                  as a precaution.
                </p>
              </td></tr>
            </table>

            <!-- What to do -->
            <table cellpadding="0" cellspacing="0" width="100%" style="margin:0 0 28px;background-color:#FEF2F2;border:1px solid #FECACA;border-radius:8px;">
              <tr><td style="padding:20px 24px;">
                <p style="margin:0 0 10px;font-size:13px;font-weight:700;color:#991B1B;">Still own this domain? Here's how to recover it</p>
                <ul style="margin:0;padding:0 0 0 20px;font-size:13px;color:#B91C1C;line-height:2;">
                  <li>Go to your VulnWatch dashboard using the button below</li>
                  <li>Find <strong>{e.DomainName}</strong> — it is still in your account marked as Revoked</li>
                  <li>Click <strong>Get New Token</strong> to generate a fresh verification token</li>
                  <li>Add the new TXT record to your DNS at
                    <code style="background:#FEE2E2;padding:1px 5px;border-radius:3px;">_vulnwatch-verify.{e.DomainName}</code>
                  </li>
                  <li>Return to the dashboard and click <strong>Verify</strong> — monitoring will be fully restored</li>
                </ul>
              </td></tr>
            </table>

            <!-- CTA -->
            <table cellpadding="0" cellspacing="0" style="margin:0 0 28px;">
              <tr>
                <td style="background-color:#0f172a;border-radius:6px;">
                  <a href="{dashboardUrl}"
                     style="display:inline-block;padding:12px 28px;font-size:14px;font-weight:600;
                            color:#ffffff;text-decoration:none;">
                    Go to Domain Dashboard →
                  </a>
                </td>
              </tr>
            </table>

            <hr style="border:none;border-top:1px solid #e4e4e7;margin:0 0 20px;"/>
            <p style="margin:0;font-size:12px;color:#a1a1aa;line-height:1.6;">
              If you did not expect this or believe this was an error, please
              <a href="mailto:support@vulnwatch.io" style="color:#71717a;">contact support</a>.
            </p>
            """);
    }
}
