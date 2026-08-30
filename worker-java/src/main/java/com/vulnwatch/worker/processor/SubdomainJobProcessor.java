package com.vulnwatch.worker.processor;

import com.vulnwatch.worker.ai.breaker.DomainCircuitBreakerAiEnricher;
import com.vulnwatch.worker.ai.interfaces.AiEnricher;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.listener.CheckpointManager;
import com.vulnwatch.worker.model.DomainFinding;
import com.vulnwatch.worker.model.DomainIntel;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.orchestrator.DomainScanOrchestrator;
import com.vulnwatch.worker.orchestrator.DomainScanOrchestrator.OrchestratorResult;
import com.vulnwatch.worker.orchestrator.mapper.SurfaceTypeMapper;
import com.vulnwatch.worker.owasp.model.OWASPEvaluationResult;
import com.vulnwatch.worker.owasp.service.OWASPEvaluator;
import com.vulnwatch.worker.persistence.OWASPPersistence;
import com.vulnwatch.worker.persistence.SubdomainPersistence;
import com.vulnwatch.worker.publisher.DomainIntelPublisher;
import com.vulnwatch.worker.state.ScanJobStateMachine;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.List;
import java.util.Set;

/**
 * Handles jobs where ScanJob.scanType() == "Subdomain" — a user explicitly scanning one
 * (or, via the API enqueuing several, all) of their discovered subdomains.
 *
 * By design, ScanJob's existing fields carry the subdomain target without any contract
 * change: job.domainId() is the Subdomains.Id row, job.domainName() is the subdomain's
 * FQDN. See the subdomain-scanning plan §0/§6 for the full contract the API producer must
 * follow.
 *
 * The recursion guard (never run SUBDOMAINS against a subdomain) lives centrally in
 * {@link SurfaceTypeMapper#resolve}, keyed off job.scanType() — NOT here. An earlier
 * version of this class tried to strip it locally and pass the filtered set to
 * primeScanContext(), but DomainScanOrchestrator.scan() independently re-resolves surfaces
 * via SurfaceTypeMapper directly from the raw job, ignoring whatever primeScanContext()
 * was given. Any guard that isn't inside the mapper itself never actually reaches scanner
 * selection — so the mapper is the only place this can correctly be enforced, and it now
 * is, for every SurfaceTypeMapper.resolve() call regardless of caller.
 *
 * Deliberately a parallel class to {@link DomainJobProcessor} rather than a branch inside
 * it — reuses DomainScanOrchestrator, OWASPEvaluator/Persistence, ScanJobStateMachine,
 * CheckpointManager and DomainIntelPublisher completely unchanged, and touches none of
 * DomainJobProcessor's own tested code path.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class SubdomainJobProcessor implements JobProcessor {

    private final DomainScanOrchestrator scanOrchestrator;
    private final AiEnricher aiEnricher;
    private final DomainCircuitBreakerAiEnricher surfaceAiEnricher;
    private final SubdomainPersistence persistence;
    private final DomainIntelPublisher publisher;
    private final ScanJobStateMachine stateMachine;
    private final CheckpointManager checkpointManager;
    private final OWASPEvaluator owaspEvaluator;
    private final OWASPPersistence owaspPersistence;
    private final SurfaceTypeMapper surfaceTypeMapper;

    @Override
    public void process(ScanJob job) {
        String scanId = job.scanId();
        log.info("Starting subdomain scan [scanId={} subdomain={}]", scanId, job.domainName());

        // SurfaceTypeMapper.resolve() already excludes SUBDOMAINS for ScanType=="Subdomain"
        // jobs — in every fallback path (empty list, unparseable list, explicit inclusion) —
        // so the set primed here is exactly what scanOrchestrator.scan() will select from
        // when it re-resolves the same job below.
        Set<SurfaceType> requested = surfaceTypeMapper.resolve(job);
        List<SurfaceType> targetSurfaces = scanOrchestrator.primeScanContext(scanId, requested);

        stateMachine.start(job, targetSurfaces);

        try {
            persistence.markRunning(scanId);
            executeScanPipeline(job);
            stateMachine.advance(job);
            checkpointManager.clear(scanId);
        } catch (Exception e) {
            handlePipelineFailure(job, e);
        } finally {
            scanOrchestrator.clearScanContext(scanId);
        }
    }

    private void executeScanPipeline(ScanJob job) {
        OrchestratorResult result = scanOrchestrator.scan(job);

        List<DomainFinding> findings = persistence.saveFindings(
                job.scanId(), job.domainId(),
                result.engineResults(), result.aiResults()
        );

        if (findings.isEmpty()) {
            log.warn("No findings persisted [scanId={}]", job.scanId());
        }

        OWASPEvaluationResult owaspResult = owaspEvaluator.evaluate(
                job.scanId(), findings,
                result.engineResults(), result.aiResults()
        );

        owaspPersistence.saveMapping(owaspResult);
        log.info("Security score (OWASP) [scanId={} score={} tier={}]",
                job.scanId(), owaspResult.overallScore(), owaspResult.tier().getLabel());

        String narrative = generateOwaspPostureBestEffort(owaspResult);
        owaspPersistence.saveNarrative(job.scanId(), narrative);

        publisher.publishSuccess(
                job,
                DomainIntel.of(job, owaspResult.overallScore(), owaspResult, surfaceAiEnricher.currentAvailability())
        );

        log.info("Subdomain scan complete [scanId={}]", job.scanId());
    }

    private String generateOwaspPostureBestEffort(OWASPEvaluationResult owaspResult) {
        try {
            return aiEnricher.posture(owaspResult);
        } catch (Exception e) {
            log.warn("OWASP posture generation failed [scanId={}]: {}", owaspResult.scanId(), e.getMessage());
            return null;
        }
    }

    private void handlePipelineFailure(ScanJob job, Exception e) {
        log.error("Subdomain scan failed [scanId={}]", job.scanId(), e);
        stateMachine.fail(job);
        persistence.markFailed(job.scanId());
        publisher.publishFailure(job, e.getMessage());
        checkpointManager.clear(job.scanId());
    }
}