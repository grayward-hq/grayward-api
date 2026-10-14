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
import com.vulnwatch.worker.engine.domain.subfinder.models.SubdomainFindings;
import com.vulnwatch.worker.persistence.DomainPersistence;
import com.vulnwatch.worker.persistence.OWASPPersistence;
import com.vulnwatch.worker.persistence.SubdomainPersistence;
import com.vulnwatch.worker.publisher.DomainIntelPublisher;
import com.vulnwatch.worker.publisher.SubdomainDiscoveryPublisher;
import com.vulnwatch.worker.state.ScanJobStateMachine;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.List;
import java.util.Set;

/**
 * Orchestrates a full domain scan pipeline from initialization to persistence and notification.
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
    private final SurfaceTypeMapper surfaceTypeMapper; // Injected to resolve types before engine init
    private final SubdomainPersistence subdomainPersistence;
    private final SubdomainDiscoveryPublisher subdomainDiscoveryPublisher;

    @Override
    public void process(ScanJob job) {
        String scanId = job.scanId();
        log.info("Starting domain scan [scanId={} domain={}]", scanId, job.domainName());

        // 1. Resolve selections and prime cache maps BEFORE updating state engines
        Set<SurfaceType> requested = surfaceTypeMapper.resolve(job);
        List<SurfaceType> targetSurfaces = scanOrchestrator.primeScanContext(scanId, requested);

        // 2. Pass the exact filtered list straight into the state machine.
        // Pass the full job (not just scanId) so the RUNNING transition can be
        // published with enough context (requestedBy) for the API to route it
        // to the right user over SignalR.
        stateMachine.start(job, targetSurfaces);

        try {
            executeScanPipeline(job);
            stateMachine.advance(job);
            checkpointManager.clear(scanId);
        } catch (Exception e) {
            handlePipelineFailure(job, e);
        } finally {
            // 3. Clear cache tracking definitions only after total workflow processing settles
            scanOrchestrator.clearScanContext(scanId);
        }
    }

    private void executeScanPipeline(ScanJob job) {
//        describeJobBestEffort(job);

        OrchestratorResult result = scanOrchestrator.scan(job);

        List<DomainFinding> findings = persistence.saveFindings(
                job.scanId(), job.domainId(),
                result.engineResults(), result.aiResults()
        );

        if (findings.isEmpty()) {
            log.warn("No findings persisted [scanId={}]", job.scanId());
        }

        // Subdomain discovery side-effect: persist + notify. Additive only — never affects
        // the findings/score/publish flow below, and does nothing if SUBDOMAINS wasn't
        // one of the surfaces requested for this scan.
        persistDiscoveredSubdomainsBestEffort(job, result);

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

        log.info("Scan complete [scanId={}]", job.scanId());
    }

    /**
     * Looks for a successful SUBDOMAINS surface result and, if present, upserts the
     * discovered hosts into "Subdomains" and publishes the discovery notification.
     * Best-effort: a failure here must never fail the parent domain scan.
     */
    private void persistDiscoveredSubdomainsBestEffort(ScanJob job, OrchestratorResult result) {
        try {
            result.engineResults().stream()
                    .filter(r -> r.surfaceType() == SurfaceType.SUBDOMAINS && r.success())
                    .findFirst()
                    .ifPresent(r -> {
                        Object raw = r.rawResult() != null ? r.rawResult().get("findings") : null;
                        if (!(raw instanceof List<?> list) || list.isEmpty()) {
                            return;
                        }
                        List<SubdomainFindings> discovered = list.stream()
                                .filter(SubdomainFindings.class::isInstance)
                                .map(SubdomainFindings.class::cast)
                                .toList();
                        if (discovered.isEmpty()) {
                            return;
                        }
                        subdomainPersistence.upsertDiscovered(job.domainId(), discovered);
                        subdomainDiscoveryPublisher.publish(job, discovered);
                    });
        } catch (Exception e) {
            log.warn("Subdomain discovery side-effect failed [scanId={}]: {}", job.scanId(), e.getMessage(), e);
        }
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
        log.error("Domain scan failed [scanId={}]", job.scanId(), e);
        stateMachine.fail(job);
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