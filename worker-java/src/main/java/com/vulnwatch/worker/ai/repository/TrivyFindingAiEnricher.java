package com.vulnwatch.worker.ai.repository;

import com.vulnwatch.worker.engine.repository.trivy.models.TrivyEngineResult;
import com.vulnwatch.worker.model.AiResult;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.ai.chat.client.ChatClient;
import org.springframework.stereotype.Service;

import java.util.List;

/**
 * AI enrichment for Trivy findings (dependency vulnerabilities and secrets).
 *
 * Replaces SpringAiRepositoryEnricher's role for the new pipeline —
 * that class enriched raw "name@version" strings from manifest parsing,
 * which no longer applies now that Trivy resolves and identifies
 * vulnerabilities directly.
 *
 * Fail-open: any AI failure returns a null-safe fallback so persistence
 * still proceeds with the scanner's own findings.
 */
@Slf4j
@Service
@RequiredArgsConstructor
public class TrivyFindingAiEnricher {

    private static final AiResult UNAVAILABLE =
            new AiResult("AI enrichment not available",
                    List.of("Review finding manually."), null);

    private final ChatClient chatClient;

    public AiResult enrichVulnerability(TrivyEngineResult finding) {
        String prompt = """
                Explain this dependency vulnerability in plain English for a developer,
                then give concrete upgrade/remediation steps.
                Package: %s
                Installed version: %s
                Fixed version: %s
                Title: %s
                Severity: %s
                """.formatted(
                safe(finding.packageName()), safe(finding.installedVersion()),
                safe(finding.fixedVersion()), safe(finding.title()), safe(finding.severity())
        );

        return callAi(prompt);
    }

    public AiResult enrichSecret(TrivyEngineResult finding) {
        return callAi(SecretRedactor.describeForAi(finding));
    }

    private AiResult callAi(String prompt) {
        try {
            String response = chatClient.prompt()
                    .system("You are a security engineer explaining scan findings to a developer. " +
                            "Be concise. Never fabricate CVE identifiers you weren't given.")
                    .user(prompt)
                    .call()
                    .content();

            if (response == null || response.isBlank()) {
                return UNAVAILABLE;
            }
            return new AiResult(response, List.of(), null);

        } catch (Exception e) {
            log.warn("Trivy finding AI enrichment failed: {}", e.getMessage());
            return UNAVAILABLE;
        }
    }

    private String safe(String value) {
        return value == null || value.isBlank() ? "unknown" : value;
    }
}