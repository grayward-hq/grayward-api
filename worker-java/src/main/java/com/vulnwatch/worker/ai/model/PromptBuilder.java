package com.vulnwatch.worker.ai.model;

import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.owasp.model.OWASPEvaluationResult;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.List;
import java.util.stream.Collectors;

@Slf4j
@Component
public class PromptBuilder {

    // Max characters of raw engine output to include in the prompt.
    // 466k tokens was hitting gpt-4o-mini's 200k TPM limit.
    // At ~4 chars/token, 6000 chars ≈ 1500 tokens — well within any model's limits.
    private static final int MAX_RAW_CHARS = 6_000;

    public String domainSystemPrompt() {
        return """
                You are a cybersecurity analyst. Analyse raw scan data from DNS, SSL,
                or HTTP header probes and return a structured assessment.
                Be concise, technical, and actionable.
                """;
    }

    public String domainDescribePrompt(ScanJob job) {
        return """
                Generate a 2-3 sentence plain-English message telling a user that their
                security scan for domain "%s" (type: %s, scan ID: %s) has started.
                Mention what kinds of checks will run. Be professional and concise.
                Do not use bullet points.
                """.formatted(job.domainName(), job.scanType(), job.scanId());
    }

    public String domainEnrichPrompt(ScanJob job, EngineResult result) {
        String findings = result.success()
                ? truncate(String.valueOf(result.rawResult()), MAX_RAW_CHARS)
                : "Engine failed: %s".formatted(result.errorMessage());

        return """
            Domain: %s
            Scan ID: %s
            Surface: %s
            Engine success: %s
            Technical findings:
            %s

            Analyse these findings and return your assessment as JSON with these fields:
            - title: a single concise sentence summarising the most significant finding
            - severity: one of Critical, High, Medium, Low, Info
            - explanation: concise technical explanation of the findings
            - cveId: CVE identifier if applicable, otherwise null
            - remediationSteps: list of actionable remediation steps
            - certExpiry: if surface is SSL, extract the certificate expiry date as an
              ISO-8601 string (e.g. "2026-01-01T00:00:00Z") from the findings, otherwise null

            Return only valid JSON, no markdown, no preamble.
            """.formatted(
                job.domainName(),
                job.scanId(),
                result.surfaceType().getLabel(),
                result.success(),
                findings);
    }

    public String repositorySystemPrompt() {
        return """
                You are a security analyst specialising in dependency vulnerability analysis.
                Analyse each dependency and return accurate, factual vulnerability data.
                Return one entry per dependency in the same order as the input.
                """;
    }

    public String repositoryEnrichPrompt(List<String> dependencies) {
        // Truncate the dependency list too — trivy output can also be very large
        String depList = truncate(String.join("\n", dependencies), MAX_RAW_CHARS);
        return """
                Analyse the following dependencies for known vulnerabilities.

                Dependencies:
                %s
                """.formatted(depList);
    }

    public String owaspPostureSystemPrompt() {
        return """
        You are a cybersecurity analyst writing an executive security posture summary.
        Use ONLY the data provided. Do not invent findings or categories.
        Return a single paragraph, 3-5 sentences, plain English, no markdown, no bullets.
        """;
    }

    public String owaspPostureUserPrompt(OWASPEvaluationResult result) {
        String categoryLines = result.categoryScores().stream()
                .map(c -> "%s (%s): score=%d, status=%s, findings=%d"
                        .formatted(
                                c.category().getCode(),
                                c.category().getDisplayName(),
                                c.score(),
                                c.status().name(),
                                c.findings().size()
                        ))
                .collect(Collectors.joining("\n"));

        return """
        Overall OWASP Score: %d/100 (%s)

        Category breakdown:
        %s

        Write a 3-5 sentence executive summary that:
        1. States the overall score and tier.
        2. Names the 1-2 lowest-scoring categories.
        3. Estimates the score improvement if those categories were remediated.
        4. Ends with a concrete prioritised action.
        """.formatted(result.overallScore(), result.tier().getLabel(), categoryLines);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    /**
     * Truncates the raw engine output to avoid exceeding model token limits.
     * Appends a note so the model knows the output was cut.
     */
    private static String truncate(String text, int maxChars) {
        if (text == null) return "";
        if (text.length() <= maxChars) return text;
        log.debug("Truncating engine output from {} to {} chars", text.length(), maxChars);
        return text.substring(0, maxChars) + "\n... [truncated for brevity]";
    }
}