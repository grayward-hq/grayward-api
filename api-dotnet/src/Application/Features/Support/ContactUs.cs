using Application.Common.Email;
using System.Net;
using Application.Features.Auth.DTOs;
using Application.Features.Support.DTOs;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Application.Features.Support;

public record ContactUsCommand(string Name,
    string Email,
    string PhoneNumber,
    string RequestType,
    string Content) : IRequest<Result<ContactUsResponse>>;

public class ContactUsCommandHandler(
    IEmailService emailService,
    IConfiguration config,
    ILogger<ContactUsCommandHandler> logger)
    : IRequestHandler<ContactUsCommand, Result<ContactUsResponse>>
{
    public async Task<Result<ContactUsResponse>> Handle(ContactUsCommand cmd, CancellationToken ct)
    {
        // Notify support/internal team
        var internalEmail = config["Contact:InternalEmail"];
        var notificationBody = BuildInternalNotificationBody(cmd);
        await emailService.SendAsync(internalEmail!, $"[{cmd.RequestType}] New Contact Request from {cmd.Name}", notificationBody);

        // Send acknowledgement to the user
        var acknowledgementBody = ContactAcknowledgementEmail.BuildBody(
            VulnwatchEmailBranding.From(config), cmd.Name);
        await emailService.SendAsync(cmd.Email, ContactAcknowledgementEmail.Subject, acknowledgementBody);

        logger.LogInformation(
            "Contact request received from {Name} ({Email}) — Type: {RequestType}",
            cmd.Name, cmd.Email, cmd.RequestType);

        return Result<ContactUsResponse>.Success(ContactUsResponse.Create("Contact request received."));
    }

    /// <summary>
    /// The internal ticket for the support team. Deliberately kept as a plain scannable table rather
    /// than the branded chrome used for recipient-facing mail: a mascot, social icons and a "Need
    /// Support?" footer are noise on a work item that staff triage.
    /// </summary>
    /// <remarks>
    /// Every field is HTML-encoded. These values are free text straight from a public form, and were
    /// previously interpolated raw — a submitted name or message containing markup rendered as live
    /// HTML in the support team's inbox.
    /// </remarks>
    private static string BuildInternalNotificationBody(ContactUsCommand cmd) => $@"
        <!DOCTYPE html>
        <html>
        <body style='font-family: Arial, sans-serif; padding: 20px;'>
            <div style='max-width: 600px; margin: auto; background: #fff; padding: 30px; border-radius: 8px; border: 1px solid #eee;'>
                <h2 style='color: #333;'>New Contact Request</h2>
                <table style='width: 100%; border-collapse: collapse; font-size: 15px;'>
                    <tr><td style='padding: 8px; color: #555; width: 140px;'><strong>Name</strong></td><td style='padding: 8px;'>{Escape(cmd.Name)}</td></tr>
                    <tr style='background:#f9f9f9'><td style='padding: 8px;'><strong>Email</strong></td><td style='padding: 8px;'>{Escape(cmd.Email)}</td></tr>
                    <tr><td style='padding: 8px;'><strong>Phone</strong></td><td style='padding: 8px;'>{Escape(cmd.PhoneNumber)}</td></tr>
                    <tr style='background:#f9f9f9'><td style='padding: 8px;'><strong>Request Type</strong></td><td style='padding: 8px;'>{Escape(cmd.RequestType)}</td></tr>
                    <tr><td style='padding: 8px; vertical-align: top;'><strong>Message</strong></td><td style='padding: 8px;'>{Escape(cmd.Content)}</td></tr>
                </table>
            </div>
        </body>
        </html>";

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}