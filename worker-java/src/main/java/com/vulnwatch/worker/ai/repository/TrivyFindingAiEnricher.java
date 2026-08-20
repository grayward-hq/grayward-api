package com.vulnwatch.worker.ai.repository;

import com.vulnwatch.worker.engine.repository.trivy.models.TrivyEngineResult;
import com.vulnwatch.worker.model.AiResult;
import io.github.resilience4j.bulkhead.Bulkhead;
import io.github.resilience4j.bulkhead.BulkheadFullException;
import io.github.resilience4j.bulkhead.BulkheadRegistry;
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
 *
 * Gated by the shared "ai-enrichment" Bulkhead (see application.properties)
 * — the same one used by domain surface enrichment, since both draw on the
 * same downstream AI provider capacity. Findings within a repo are enriched
 * sequentially, so this mainly protects against many concurrent repository
 * jobs each racing through their own finding list at once.
 */
@Slf4j
@Service
@RequiredArgsConstructor
public class TrivyFindingAiEnricher {

    private static final String AI_BULKHEAD = "ai-enrichment";

    private static final AiResult UNAVAILABLE =
            new AiResult("AI enrichment not available",
                    List.of("Review finding manually."), null);

    private final ChatClient chatClient;
    private final BulkheadRegistry bulkheadRegistry;

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
        Bulkhead bulkhead = bulkheadRegistry.bulkhead(AI_BULKHEAD);
        try {
            bulkhead.acquirePermission();
        } catch (BulkheadFullException e) {
            log.warn("AI enrichment bulkhead saturated too long, using fallback");
            return UNAVAILABLE;
        }

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
        } finally {
            bulkhead.onComplete();
        }
    }

    private String safe(String value) {
        return value == null || value.isBlank() ? "unknown" : value;
    }
}