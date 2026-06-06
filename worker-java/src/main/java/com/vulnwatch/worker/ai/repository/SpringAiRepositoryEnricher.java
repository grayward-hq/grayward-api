package com.vulnwatch.worker.ai.repository;

import com.vulnwatch.worker.ai.model.PromptBuilder;
import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.model.DependencyFinding;
import com.vulnwatch.worker.model.ScanJob;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.ai.chat.client.ChatClient;
import org.springframework.core.ParameterizedTypeReference;
import org.springframework.stereotype.Service;

import java.util.List;

/**
 * Repository dependency enricher — replaces AnthropicEnricher.
 *
 * Receives a list of "name@version" dependency strings parsed from a manifest
 * (package.json, pom.xml, etc.) and returns one DependencyFinding per input,
 * preserving order.
 *
 * Uses .entity(new ParameterizedTypeReference<List<DependencyFinding>>() {})
 * so Spring AI's BeanOutputConverter derives the schema for both the List
 * wrapper and the DependencyFinding record, then deserialises directly.
 * No manual JSON parsing, no JsonNode traversal.
 *
 * Fail-open: if AI is unavailable, returns a fallback DependencyFinding per
 * dependency (hasVulnerabilities=false, severity=NONE) so the scan can still
 * persist and notify C# rather than silently dying.
 *
 * Injected into RepositoryJobProcessor in place of AnthropicEnricher —
 * the call signature enrich(List<String>, ScanJob) is identical.
 */
@Slf4j
@Service
@RequiredArgsConstructor
public class SpringAiRepositoryEnricher {

    private final ChatClient chatClient;
    private final PromptBuilder promptBuilder;

    /**
     * Analyses a batch of "name@version" dependency strings for vulnerabilities.
     *
     * @param dependencies list of "name@version" strings, e.g. ["lodash@4.17.21"]
     * @param job  the originating scan job (used for logging only)
     * @return one DependencyFinding per input in the same order
     */
    public List<DependencyFinding> enrich(List<String> dependencies, ScanJob job) {
        if (dependencies.isEmpty()) {
            log.debug("[{}] No dependencies to enrich", job.scanId());
            return List.of();
        }

        log.info("[{}] Sending {} dependencies to AI for enrichment",
                job.scanId(), dependencies.size());

        try {
            List<DependencyFinding> findings = chatClient.prompt()
                    .system(promptBuilder.repositorySystemPrompt())
                    .user(promptBuilder.repositoryEnrichPrompt(dependencies))
                    .call()
                    .entity(new ParameterizedTypeReference<List<DependencyFinding>>() {});

            if (findings == null || findings.isEmpty()) {
                log.warn("[{}] AI returned empty findings list — using fallbacks", job.scanId());
                return fallbackList(dependencies, "AI returned empty response");
            }

            long vulnerable = findings.stream()
                    .filter(DependencyFinding::hasVulnerabilities)
                    .count();
            log.info("[{}] Enrichment complete. deps={} vulnerable={}",
                    job.scanId(), findings.size(), vulnerable);

            return findings;

        } catch (Exception e) {
            log.error("[{}] AI repository enrichment failed: {}", job.scanId(), e.getMessage(), e);
            return fallbackList(dependencies, "AI enrichment unavailable");
        }
    }


    /**
     * Produces a safe fallback list when the AI call fails entirely.
     * Preserves the dependency list so the scan can still persist and
     * notify C# — findings will show NONE severity and the reason as summary.
     */
    private List<DependencyFinding> fallbackList(List<String> dependencies, String reason) {
        return dependencies.stream()
                .map(raw -> fallback(raw, reason))
                .toList();
    }

    private DependencyFinding fallback(String raw, String reason) {
        // "name@version" → split on last @ to handle scoped npm packages like @scope/pkg@1.0.0
        int lastAt = raw.lastIndexOf('@');
        String name    = lastAt > 0 ? raw.substring(0, lastAt) : raw;
        String version = lastAt > 0 ? raw.substring(lastAt + 1) : "unknown";

        return new DependencyFinding(
                name,
                version,
                raw,
                false,
                FindingSeverity.NONE.getName(),
                List.of(),
                reason,
                "Retry scan or check manually."
        );
    }
}