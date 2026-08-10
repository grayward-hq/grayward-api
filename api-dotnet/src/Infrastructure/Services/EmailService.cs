using System.Net;
using System.Net.Mail;
using Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string body)
    {
        SmtpCredentials? credentials;

        try
        {
            credentials = SmtpCredentials.Load(_config);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Email service is misconfigured. Email to {Recipient} was not sent.", to);
            return;
        }

        using var client = new SmtpClient(credentials.Host, credentials.Port)
        {
            Credentials = new NetworkCredential(credentials.Username, credentials.Password),
            // STARTTLS, not implicit TLS — SmtpClient cannot do the latter, so this must be an
            // explicit-TLS port (587/2525/25). Pointing it at 465 hangs until the timeout below.
            EnableSsl = true,
            // The 100s default ties up the request thread when a connection stalls.
            Timeout = 30_000
        };

        try
        {
            var mail = new MailMessage
            {
                From = new MailAddress(credentials.Username, credentials.FromName),
                To = { new MailAddress(to) },
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };


            await client.SendMailAsync(mail);

            _logger.LogInformation("Email sent to {Recipient} with subject '{Subject}'.", to, subject);
            return;
        }
        catch (SmtpException ex)
        {
            // Host, port and sender are logged because SmtpException collapses most server-side
            // rejections into GeneralFailure: without them there is no way to tell a wrong port from
            // bad credentials from an unverified sending domain. The password is never logged.
            _logger.LogError(
                ex,
                "SMTP failure while sending email to {Recipient} via {Host}:{Port} as {Sender}. Status: {StatusCode}.",
                to, credentials.Host, credentials.Port, credentials.Username, ex.StatusCode);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while sending email to {Recipient} via {Host}:{Port}.",
                to, credentials.Host, credentials.Port);
            return;
        }
    }
}

internal sealed record SmtpCredentials(string FromName, string Host, int Port, string Username, string Password)
{
    public static SmtpCredentials Load(IConfiguration config)
    {
        var fromName = config["SmtpCredentials:FromName"];
        var host = config["SmtpCredentials:Host"];
        var portRaw = config["SmtpCredentials:Port"];
        var username = config["SmtpCredentials:Username"];
        var password = config["SmtpCredentials:Password"];

        if (string.IsNullOrWhiteSpace(fromName))
            throw new InvalidOperationException("SMTP host is not configured.");

        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("SMTP host is not configured.");

        if (!int.TryParse(portRaw, out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException($"SMTP port '{portRaw}' is invalid.");

        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("SMTP username is not configured.");

        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("SMTP password is not configured.");

        return new(fromName, host, port, username, password);
    }
}