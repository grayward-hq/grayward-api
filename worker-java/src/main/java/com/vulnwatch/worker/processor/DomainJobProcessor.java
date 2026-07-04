package com.vulnwatch.worker.processor;

import com.vulnwatch.worker.ai.breaker.DomainCircuitBreakerAiEnricher;
import com.vulnwatch.worker.ai.interfaces.AiEnricher;
import com.vulnwatch.worker.listener.CheckpointManager;
import com.vulnwatch.worker.model.DomainFinding;
import com.vulnwatch.worker.model.DomainIntel;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.orchestrator.DomainScanOrchestrator;
import com.vulnwatch.worker.orchestrator.DomainScanOrchestrator.OrchestratorResult;
import com.vulnwatch.worker.owasp.model.OWASPEvaluationResult;
import com.vulnwatch.worker.owasp.service.OWASPEvaluator;
import com.vulnwatch.worker.persistence.DomainPersistence;
import com.vulnwatch.worker.persistence.OWASPPersistence;
import com.vulnwatch.worker.publisher.DomainIntelPublisher;
import com.vulnwatch.worker.state.ScanJobStateMachine;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.List;

/**
 * Orchestrates a full domain scan pipeline from initialization to persistence and notification.
 *
 * Score responsibility:
 *   SecurityScore and OWASPScore on the Scans row are both written by OWASPPersistence.saveMapping()
 *   after OWASP evaluation completes. There is no separate "raw" score computation here —
 *   the OWASP overall score IS the security score.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class DomainJobProcessor implements JobProcessor {

    private final DomainScanOrchestrator scanOrchestrator;
    private final AiEnricher aiEnricher;
    private final DomainCircuitBreakerAiEnricher surfaceAiEnricher;
    private final DomainPersistence persistence;
    private final DomainIntelPublisher publisher;
    private final ScanJobStateMachine stateMachine;
    private final CheckpointManager checkpointManager;
    private final OWASPEvaluator owaspEvaluator;
    private final OWASPPersistence owaspPersistence;

    @Override
    public void process(ScanJob job) {
        String scanId = job.scanId();
        log.info("Starting domain scan [scanId={} domain={}]", scanId, job.domainName());

        stateMachine.start(scanId, scanOrchestrator.registeredSurfaces());

        try {
            executeScanPipeline(job);
            stateMachine.advance(scanId);
            checkpointManager.clear(scanId);
        } catch (Exception e) {
            handlePipelineFailure(job, e);
        }
    }

    private void executeScanPipeline(ScanJob job) {

        describeJobBestEffort(job);                                              // step 1

        OrchestratorResult result = scanOrchestrator.scan(job);                  // step 2

        // step 3 — persist findings; scan status set to Completed
        List<DomainFinding> findings = persistence.saveFindings(
                job.scanId(), job.domainId(),
                result.engineResults(), result.aiResults()
        );

        if (findings.isEmpty()) {
            log.warn("No findings persisted [scanId={}]", job.scanId());
        }

        // step 4 — rule-based OWASP mapping
        OWASPEvaluationResult owaspResult = owaspEvaluator.evaluate(
                job.scanId(), findings,
                result.engineResults(), result.aiResults()
        );

        // step 5 — persist OWASP mappings and write SecurityScore + OWASPScore from OWASP result
        owaspPersistence.saveMapping(owaspResult);
        log.info("Security score (OWASP) [scanId={} score={} tier={}]",
                job.scanId(), owaspResult.overallScore(), owaspResult.tier().getLabel());

        // step 6 — AI-generated posture narrative (best-effort, never throws)
        String narrative = generateOwaspPostureBestEffort(owaspResult);

        // step 7 — persist narrative
        owaspPersistence.saveNarrative(job.scanId(), narrative);

        // step 8 — publish to .NET API via Redis
        publisher.publishSuccess(
                job,
                DomainIntel.of(job, owaspResult.overallScore(), owaspResult, surfaceAiEnricher.currentAvailability())
        );

        log.info("Scan complete [scanId={}]", job.scanId());
    }

    private String generateOwaspPostureBestEffort(OWASPEvaluationResult owaspResult) {
        try {
            return aiEnricher.posture(owaspResult);
        } catch (Exception e) {
            log.warn("OWASP posture generation failed [scanId={}]: {}",
                    owaspResult.scanId(), e.getMessage());
            return null;
        }
    }

    private void handlePipelineFailure(ScanJob job, Exception e) {
        log.error("Domain scan failed [scanId={}]", job.scanId(), e);
        stateMachine.fail(job.scanId());
        publisher.publishFailure(job, e.getMessage());
        checkpointManager.clear(job.scanId());
    }

    private void describeJobBestEffort(ScanJob job) {
        try {
            String description = aiEnricher.describe(job);
            if (description != null) {
                log.info("Job description [scanId={}]: {}", job.scanId(), description);
            }
        } catch (Exception e) {
            log.warn("Could not generate description [scanId={}]: {}", job.scanId(), e.getMessage());
        }
    }
}
