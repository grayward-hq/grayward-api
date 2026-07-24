package com.vulnwatch.worker.orchestrator;

import com.vulnwatch.worker.ai.repository.TrivyFindingAiEnricher;
import com.vulnwatch.worker.engine.repository.trivy.TrivyEngine;
import com.vulnwatch.worker.engine.repository.trivy.models.TrivyEngineResult;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.RepositoryMetadata;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.persistence.RepositoryFindingsPersistence;
import com.vulnwatch.worker.persistence.RepositoryMetadataRepository;
import com.vulnwatch.worker.retry.RepositoryScanRetryPolicy;
import com.vulnwatch.worker.service.github.GitHubInstallationTokenService;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.ArrayList;
import java.util.List;

/**
 * Facade coordinating a full repository scan: resolve identity → mint auth
 * (if private) → run Trivy with retry → enrich findings → persist.
 *
 * Self-contained within the worker — no dependency on any API-side change,
 * consumer, or payload field that doesn't already exist in ScanJob.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class RepositoryScanOrchestrator {

    private final RepositoryMetadataRepository metadataRepository;
    private final GitHubInstallationTokenService tokenService;
    private final TrivyEngine trivyEngine;
    private final RepositoryScanRetryPolicy retryPolicy;
    private final TrivyFindingAiEnricher aiEnricher;
    private final RepositoryFindingsPersistence persistence;

    public void scan(ScanJob job) {
        String scanId = job.scanId();

        try {
            persistence.markRunning(scanId);
            RepositoryMetadata metadata = metadataRepository.findById(job.repoId())
                    .orElseThrow(() -> new IllegalStateException(
                            "No MonitoredRepository found for id=%s".formatted(job.repoId())));

            String token = metadata.requiresAuth()
                    ? tokenService.getInstallationToken(metadata.installationId())
                    : null;

            List<TrivyEngineResult> findings = retryPolicy.execute(scanId, () ->
                    trivyEngine.scan(metadata, token, scanId));

            List<AiResult> enrichments = enrichAll(findings);

            persistence.saveFindings(scanId, findings, enrichments);

            log.info("Repository scan complete [scanId={} repo={} findings={}]",
                    scanId, metadata.fullName(), findings.size());

        } catch (Exception e) {
            log.error("Repository scan failed [scanId={}]: {}", scanId, e.getMessage(), e);
            persistence.markFailed(scanId);
        }
    }

    private List<AiResult> enrichAll(List<TrivyEngineResult> findings) {
        List<AiResult> results = new ArrayList<>(findings.size());
        for (TrivyEngineResult finding : findings) {
            boolean isDependency = finding.packageName() != null;
            results.add(isDependency
                    ? aiEnricher.enrichVulnerability(finding)
                    : aiEnricher.enrichSecret(finding));
        }
        return results;
    }
}