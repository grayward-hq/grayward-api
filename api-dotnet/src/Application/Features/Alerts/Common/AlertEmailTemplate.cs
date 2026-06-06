namespace Application.Features.Alerts.Common;

public static class AlertEmailTemplates
{
    public static string Wrap(string title, string previewText, string innerContent) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8"/>
          <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
          <title>{title}</title>
        </head>
        <body style="margin:0;padding:0;background-color:#f4f4f5;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;">
        <div style="display:none;max-height:0px;overflow:hidden;opacity:0;color:transparent;;">>
          {previewText}
        </div>
          <table width="100%" cellpadding="0" cellspacing="0" style="padding:40px 20px;">
            <tr>
              <td align="center">
                <table width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;">

                  <!-- Header -->
                  <tr>
                    <td style="background-color:#0f172a;padding:32px 40px;border-radius:12px 12px 0 0;text-align:center;">
                      <span style="font-size:22px;font-weight:700;color:#ffffff;">🔐 VulnWatch</span>
                    </td>
                  </tr>

                  <!-- Body -->
                  <tr>
                    <td style="background-color:#ffffff;padding:40px;border-left:1px solid #e4e4e7;border-right:1px solid #e4e4e7;">
                      {innerContent}
                    </td>
                  </tr>

                  <!-- Footer -->
                  <tr>
                    <td style="background-color:#f4f4f5;padding:24px 40px;border-radius:0 0 12px 12px;border:1px solid #e4e4e7;border-top:none;text-align:center;">
                      <p style="margin:0 0 4px;font-size:12px;color:#a1a1aa;">VulnWatch — Vulnerability Monitoring Platform</p>
                      <p style="margin:0;font-size:12px;color:#a1a1aa;">This is an automated message, please do not reply.</p>
                    </td>
                  </tr>

                </table>
              </td>
            </tr>
          </table>
        </body>
        </html>
        """;
}