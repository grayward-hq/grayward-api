using Application.Features.Auth.DTOs;
using Application.Features.Compliance.DTOs;
using Application.Helpers;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using MediatR;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Microsoft.Extensions.Logging;
using Application.Services;

namespace Application.Features.Compliance;

public record GenerateReportCommand(Guid DomainId) : IRequest<Result<byte[]>>;

public class GenerateReportHandler(
    IScanRepository scanRepo,
    IDomainRepository domainRepo,
    ICurrentUser currentUser,
    OwaspEvaluationEngine owaspEngine,
    ClaudeService chatService
    // ILogger<GenerateReportHandler> logger
    )
    : IRequestHandler<GenerateReportCommand, Result<byte[]>>
{
    public async Task<Result<byte[]>> Handle(
        GenerateReportCommand cmd, CancellationToken ct)
    {
        var domain = await domainRepo.FindUserDomainById(
            currentUser.UserId, cmd.DomainId, ct);

        if (domain is null)
            return Result<byte[]>.Failure(Error.NotFound("Domain not found."));

        var scan = await scanRepo.FindLatestCompletedByDomain(cmd.DomainId, ct);

        if (scan is null)
            return Result<byte[]>.Failure(
                Error.NotFound("No completed scan found for this domain."));

        var scanWithFindings = await scanRepo.FindByIdWithFindings(scan.Id, ct);

        var deduplicatedFindings = scanWithFindings!.Findings
            .GroupBy(f => new { f.Surface, f.Title })
            .Select(g => g.First())
            .ToList();

        var owaspResult = owaspEngine.Evaluate(deduplicatedFindings);

    //     logger.LogInformation("=== OWASP RESULT ===\nScore: {Score} | Tier: {Tier}\nCategories:\n{Categories}",
    // owaspResult.OverallScore,
    // owaspResult.ComplianceTier,
    // string.Join("\n", owaspResult.Categories.Select(c =>
    //     $"  {c.Code} {c.Name} | Status: {c.ComplianceStatus} | Score: {c.Score} | Details: {string.Join(", ", c.TechnicalDetails)}")));

        var prompt = BuildSummaryPrompt(domain.DomainName, scanWithFindings, owaspResult, deduplicatedFindings);

        // logger.LogInformation("=== SUMMARY PROMPT ===\n{Prompt}", prompt);

        var executiveSummary = await chatService.Chat(
    """
    You are a security analyst writing executive summaries for scan reports.
    STRICT RULES — violating any rule means your response is rejected:
    - Write EXACTLY 3 sentences. Not 2, not 4. Exactly 3.
    - NO bullet points. NO numbered lists. NO headers. NO markdown. NO bold text.
    - Use ONLY the scan data provided. Do not invent threats or generic advice.
    - Reference the OWASP score and at least two specific finding titles by name.
    - End the third sentence with the single highest-priority remediation action.
    - Plain prose only. Your entire response is the 3 sentences and nothing else.
    """,
    [],
    prompt,
    ct);

        // logger.LogInformation("=== SUMMARY RESPONSE ===\n{Response}", executiveSummary);

        var pdfBytes = BuildPdf(domain, scanWithFindings, owaspResult, executiveSummary, deduplicatedFindings);
        return Result<byte[]>.Success(pdfBytes);
    }

    private byte[] BuildPdf(ScannedDomain domain, Scan scan,
        OwaspEvaluationResult owasp, string summary, List<Finding> findings)
    {
        // QuestPDF document definition
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, QuestPDF.Infrastructure.Unit.Centimetre);

                page.Header().Element(c => ComposeHeader(c, domain.DomainName));
                page.Content().Element(c => 
                ComposeContent(c, domain, scan, owasp, summary, findings));
                page.Footer().Element(ComposeFooter);
            });
        });

        return document.GeneratePdf();
    }

    private void ComposeHeader(IContainer container, string domainName)
    {
        container.PaddingBottom(10).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
            .Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("VulnWatch").FontSize(18).Bold()
                        .FontColor(Colors.Grey.Darken4);
                    col.Item().Text($"Security Report — {domainName}")
                        .FontSize(10).FontColor(Colors.Grey.Medium);
                });
            });
    }

    private string BuildSummaryPrompt(string domainName, Scan scan, OwaspEvaluationResult owasp, List<Finding> findings)
{
    // OWASP non-compliant categories
    var nonCompliantCategories = owasp.Categories
        .Where(c => c.ComplianceStatus != "Compliant")
        .Select(c =>
        {
            var details = c.TechnicalDetails.Any()
                ? $" ({string.Join("; ", c.TechnicalDetails.Take(3))})"
                : "";
            return $"- {c.Code} {c.Name} [{c.ComplianceStatus}]{details}";
        })
        .ToList();

    var owaspSection = nonCompliantCategories.Any()
        ? string.Join("\n", nonCompliantCategories)
        : "All OWASP categories passed.";

    // Open findings grouped by surface with technical details
    var openFindings = findings  // ← use param instead of scan.Findings
        .Where(f => f.Status == FindingStatus.Open)
        .OrderBy(f => f.Severity)
        .ToList();

    var findingsSection = openFindings.Any()
        ? string.Join("\n", openFindings.Select(f =>
        {
            var enriched = EnrichedFinding.From(f);
            var techDetail = BuildFindingDetail(enriched);
            var detail = string.IsNullOrWhiteSpace(techDetail) ? "" : $" → {techDetail}";
            return $"- [{f.Severity}] {f.Surface}: {f.Title}{detail}";
        }))
        : "No open findings.";

    // Severity counts for context
    var critical = openFindings.Count(f => f.Severity == FindingSeverity.Critical);
    var high     = openFindings.Count(f => f.Severity == FindingSeverity.High);
    var medium   = openFindings.Count(f => f.Severity == FindingSeverity.Medium);
    var low      = openFindings.Count(f => f.Severity == FindingSeverity.Low);

    var instruction = openFindings.Any()
        ? "Reference the OWASP score and at least two specific findings by name. End with the single highest-priority remediation action."
        : "Acknowledge the clean scan. Mention what protections are confirmed to be in place. End with a recommendation to maintain this posture.";

    return $"""
        You are writing an executive summary for a security scan report. Use ONLY the data below.
        Do NOT give generic advice. Write exactly 3 sentences. No bullets. No headers. No markdown.

        Domain: {domainName}
        Scan Date: {scan.CompletedAt:MMMM dd, yyyy}
        OWASP Score: {owasp.OverallScore}/100 — {owasp.ComplianceTier}

        Finding counts: {critical} Critical, {high} High, {medium} Medium, {low} Low

        Open Findings:
        {findingsSection}

        OWASP Category Results:
        {owaspSection}

        {instruction}
        """;
}
    private void ComposeContent(IContainer container, ScannedDomain domain, Scan scan,
    OwaspEvaluationResult owasp, string executiveSummary, List<Finding> findings)
    {
        container.PaddingVertical(10).Column(col =>
        {
            col.Spacing(20);

            // ── Cover info ────────────────────────────────────────────
            col.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(16).Column(c =>
            {
                c.Item().Text("Security Report").FontSize(18).Bold();
                c.Item().Text(domain.DomainName).FontSize(14).FontColor(Colors.Grey.Darken2);
                c.Item().Text($"Scanned: {scan.CompletedAt:dddd, MMMM dd, yyyy}").FontSize(10).FontColor(Colors.Grey.Medium);
                c.Item().Text($"Report ID: {scan.Id:N}").FontSize(9).FontColor(Colors.Grey.Lighten1);
            });

            // ── Executive summary ─────────────────────────────────────
            col.Item().Column(c =>
            {
                c.Item().PaddingBottom(6).Text("Executive Summary").FontSize(14).Bold();
                c.Item().Text(executiveSummary).FontSize(10).LineHeight(1.6f);
            });

            // ── OWASP score card ──────────────────────────────────────
            col.Item().Column(c =>
            {
                c.Item().PaddingBottom(6).Text("OWASP Security Assessment").FontSize(14).Bold();

                c.Item().Row(row =>
                {
                    row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2)
                        .Padding(12).Column(inner =>
                        {
                            inner.Item().Text("Overall Score").FontSize(9).FontColor(Colors.Grey.Medium);
                            inner.Item().Text($"{owasp.OverallScore}/100")
                                .FontSize(28).Bold()
                                .FontColor(ScoreColor(owasp.OverallScore));
                            inner.Item().Text(owasp.ComplianceTier)
                                .FontSize(11)
                                .FontColor(ScoreColor(owasp.OverallScore));
                        });

                    row.ConstantItem(12);

                    row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2)
                        .Padding(12).Column(inner =>
                        {
                            inner.Item().PaddingBottom(4).Text("Category Breakdown")
                                .FontSize(9).FontColor(Colors.Grey.Medium);

                            foreach (var category in owasp.Categories.OrderBy(c => c.Score))
                            {
                                inner.Item().Row(r =>
                                {
                                    r.RelativeItem().Text($"{category.Code} — {category.Name}").FontSize(8);
                                    r.ConstantItem(50).AlignRight()
                                        .Text($"{category.Score}/100").FontSize(8)
                                        .FontColor(StatusColor(category.ComplianceStatus));
                                });

                                // Show payload details for non-compliant categories
                                if (category.TechnicalDetails.Any() && category.ComplianceStatus != "Compliant")
                                {
                                    foreach (var detail in category.TechnicalDetails.Take(3))
                                    {
                                        inner.Item().PaddingLeft(12).Text($"↳ {detail}")
                                            .FontSize(7).FontColor(Colors.Grey.Medium);
                                    }
                                }
                            }
                        });
                });
            });


            // ── Findings ──────────────────────────────────────────────
            col.Item().Column(c =>
            {
                c.Item().PaddingBottom(6).Text("Findings").FontSize(14).Bold();

                var openFindings = findings 
                    .Where(f => f.Status == FindingStatus.Open)
                    .OrderBy(f => f.Severity)
                    .ToList();

                if (!openFindings.Any())
                {
                    c.Item().Text("No open findings — all checks passed.")
                        .FontSize(10).FontColor(Colors.Green.Darken2);
                }
                else
                {
                    // Table header
                    c.Item().Border(1).BorderColor(Colors.Grey.Lighten2)
                        .Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(60);   // severity
                                cols.ConstantColumn(70);   // surface
                                cols.RelativeColumn(2);    // title
                                cols.RelativeColumn(3);    // details ← new
                            });

                            // Header row
                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6)
                                    .Text("Severity").FontSize(9).Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6)
                                    .Text("Surface").FontSize(9).Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6)
                                    .Text("Title").FontSize(9).Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6)
                                    .Text("Details").FontSize(9).Bold();
                            });

                            // Data rows
                            foreach (var finding in openFindings)
                            {
                                var enriched = EnrichedFinding.From(finding);
                                var detail = BuildFindingDetail(enriched);

                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3)
                                    .Padding(6)
                                    .Text(finding.Severity.ToString())
                                    .FontSize(8)
                                    .FontColor(SeverityColor(finding.Severity));

                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3)
                                    .Padding(6)
                                    .Text(finding.Surface.ToString()).FontSize(8);

                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3)
                                    .Padding(6)
                                    .Text(finding.Title).FontSize(8);

                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3)
                                    .Padding(6).Text(detail).FontSize(7).FontColor(Colors.Grey.Darken2);
                            }


                        });
                }
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.BorderTop(1).BorderColor(Colors.Grey.Lighten2)
            .PaddingTop(8)
            .Row(row =>
            {
                row.RelativeItem().Text("VulnWatch — Vulnerability Monitoring Platform")
                    .FontSize(8).FontColor(Colors.Grey.Medium);

                row.RelativeItem().AlignCenter()
                    .Text($"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC")
                    .FontSize(8).FontColor(Colors.Grey.Medium);

                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                    text.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
    }

    private static string BuildFindingDetail(EnrichedFinding e)
    {
        if (e.HttpHeaders is { } h)
        {
            var parts = new List<string>();
            if (h.MissingHeaders.Any())
                parts.Add($"Missing: {string.Join(", ", h.MissingHeaders.Take(3))}");
            if (h.ServerHeader != null)
                parts.Add($"Server: {h.ServerHeader}");
            return string.Join(" | ", parts);
        }

        if (e.Ssl is { } ssl)
        {
            var parts = new List<string> { ssl.Protocol, ssl.CipherSuite };
            if (ssl.DaysUntilExpiry < 60)
                parts.Add($"Expires in {ssl.DaysUntilExpiry}d");
            return string.Join(" | ", parts);
        }

        if (e.Dns is { } dns)
        {
            var missing = new List<string>();
            if (!dns.HasSPF) missing.Add("SPF");
            if (!dns.HasDMARC) missing.Add("DMARC");
            if (!dns.HasMX) missing.Add("MX");
            return missing.Any()
                ? $"Missing records: {string.Join(", ", missing)}"
                : "All DNS records present";
        }

        return string.Empty;
    }
    private static string ScoreColor(int score) => score switch
    {
        >= 90 => Colors.Green.Darken2,
        >= 75 => Colors.Blue.Darken2,
        >= 50 => Colors.Orange.Darken2,
        _ => Colors.Red.Darken2
    };

    private static string StatusColor(string status) => status switch
    {
        "Compliant" => Colors.Green.Darken2,
        "PartiallyCompliant" => Colors.Orange.Darken2,
        "NonCompliant" => Colors.Red.Darken2,
        _ => Colors.Grey.Medium
    };

    private static string SeverityColor(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Critical => Colors.Red.Darken2,
        FindingSeverity.High => Colors.Orange.Darken2,
        FindingSeverity.Medium => Colors.Yellow.Darken3,
        FindingSeverity.Low => Colors.Green.Darken1,
        _ => Colors.Grey.Medium
    };

}