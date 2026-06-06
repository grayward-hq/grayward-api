package com.vulnwatch.worker.ai.model;

import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.model.payload.DnsPayload;
import com.vulnwatch.worker.model.payload.HttpPayload;
import com.vulnwatch.worker.model.payload.SslPayload;
import org.springframework.stereotype.Component;

import java.util.List;
import java.util.stream.Collectors;

@Component
public class PromptBuilder {


    public String domainSystemPrompt() {
        return """
                You are a cybersecurity analyst. Analyse raw scan data from DNS, SSL,
                or HTTP header probes and return a structured assessment.
                Be concise, technical, and actionable.
                """;
    }

    public String domainEnrichPrompt(ScanJob job, EngineResult result) {
        return """
                Domain: %s
                Scan ID: %s
                Surface: %s
                Engine success: %s
                Technical findings:
                %s

                Analyse these findings and return your assessment.
                """.formatted(
                job.domainName(),
                job.scanId(),
                result.surface(),
                result.success(),
                formatSurfacePayload(result));
    }

    public String domainDescribePrompt(ScanJob job) {
        return """
                Generate a 2-3 sentence plain-English message telling a user that their
                security scan for domain "%s" (type: %s, scan ID: %s) has started.
                Mention what kinds of checks will run. Be professional and concise.
                Do not use bullet points.
                """.formatted(job.domainName(), job.scanType(), job.scanId());
    }

    public String repositorySystemPrompt() {
        return """
                You are a security analyst specialising in dependency vulnerability analysis.
                Analyse each dependency and return accurate, factual vulnerability data.
                Return one entry per dependency in the same order as the input.
                """;
    }

    public String repositoryEnrichPrompt(List<String> dependencies) {
        return """
                Analyse the following dependencies for known vulnerabilities.

                Dependencies:
                %s
                """.formatted(String.join("\n", dependencies));
    }



    private String formatSurfacePayload(EngineResult result) {
        if (!result.success())
            return "Engine failed: %s".formatted(result.errorMessage());

        return switch (result.payload()) {
            case DnsPayload dns -> """
                    SPF: %s | DMARC: %s | MX: %s
                    Issues: %s
                    Raw records:
                    %s
                    """.formatted(
                    dns.hasSPF(), dns.hasDMARC(), dns.hasMX(),
                    dns.issues().isEmpty() ? "none" : String.join(", ", dns.issues()),
                    dns.rawRecords().entrySet().stream()
                            .map(e -> "  %s: %s".formatted(e.getKey(), e.getValue()))
                            .collect(Collectors.joining("\n")));

            case SslPayload ssl -> """
                    Protocol: %s | Cipher: %s
                    Subject: %s | Expiry: %s (%d days)
                    Self-signed: %s | Expired: %s
                    Issues: %s
                    """.formatted(
                    ssl.protocol(), ssl.cipherSuite(),
                    ssl.certSubject(), ssl.certExpiry(), ssl.daysUntilExpiry(),
                    ssl.isSelfSigned(), ssl.isExpired(),
                    ssl.issues().isEmpty() ? "none" : String.join(", ", ssl.issues()));

            case HttpPayload http -> """
                    Status: %d | Server: %s
                    Present headers: %s
                    Missing headers: %s
                    Exposed technology: %s
                    Issues: %s
                    """.formatted(
                    http.statusCode(), http.serverHeader(),
                    http.presentHeaders().isEmpty() ? "none" : String.join(", ", http.presentHeaders()),
                    http.missingHeaders().isEmpty() ? "none" : String.join(", ", http.missingHeaders()),
                    http.exposedTechnology() != null ? http.exposedTechnology() : "none",
                    http.issues().isEmpty() ? "none" : String.join(", ", http.issues()));
        };
    }
}