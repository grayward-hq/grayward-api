package com.vulnwatch.worker.processor;

import com.vulnwatch.worker.ai.breaker.DomainCircuitBreakerAiEnricher;
import com.vulnwatch.worker.ai.interfaces.AiEnricher;
import com.vulnwatch.worker.enums.FindingSeverity;
import com.vulnwatch.worker.listener.CheckpointManager;
import com.vulnwatch.worker.model.AiResult;
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
import java.util.Objects;

/**
 * Orchestrates a full domain scan pipeline from initialization to persistence and notification.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class DomainJobProcessor implements JobProcessor {

    private static final int BASE_SECURITY_SCORE = 100;

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

        // Pass surfaces derived from registered scanners — not hardcoded
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

        describeJobBestEffort(job);                                         // step 1

        OrchestratorResult result = scanOrchestrator.scan(job);            // step 2

        int score = computeScore(result.aiResults());                       // step 3
        log.info("Security score [scanId={} score={}]", job.scanId(), score);

        // step 4 — unchanged except DomainFinding now carries id
        List<DomainFinding> findings = persistence.saveFindings(
                job.scanId(), job.domainId(),
                result.engineResults(), result.aiResults(), score
        );

        if (findings.isEmpty()) {
            log.warn("No findings persisted [scanId={}]", job.scanId());
        }

        // step 5 — rule-based OWASP mapping
        OWASPEvaluationResult owaspResult = owaspEvaluator.evaluate(
                job.scanId(), findings,
                result.engineResults(), result.aiResults()
        );

        // step 6 — persist mapping rows + score/tier on Scans
        owaspPersistence.saveMapping(owaspResult);

        // step 7 — one AI call for the posture narrative (best-effort)
        String narrative = generateOwaspPostureBestEffort(owaspResult);

        // step 8 — persist narrative (best-effort, never throws)
        owaspPersistence.saveNarrative(job.scanId(), narrative);

        // step 9 — publish
        publisher.publishSuccess(
                job,
                DomainIntel.of(job, score, owaspResult, surfaceAiEnricher.currentAvailability())
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
        // Leave checkpoint in place on unexpected failure for crash-resume tracking
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

    private int computeScore(List<AiResult> enrichments) {
        int deductions = enrichments.stream()
                .filter(Objects::nonNull)
                .map(AiResult::severity)
                .map(FindingSeverity::fromName)
                .mapToInt(FindingSeverity::getDeduction)
                .sum();

        return Math.max(0, BASE_SECURITY_SCORE - deductions);
    }
}