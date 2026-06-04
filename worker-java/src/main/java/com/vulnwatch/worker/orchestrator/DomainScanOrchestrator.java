package com.vulnwatch.worker.orchestrator;

import com.vulnwatch.worker.ai.breaker.DomainCircuitBreakerAiEnricher;
import com.vulnwatch.worker.engine.domain.Scanner;
import com.vulnwatch.worker.enums.SurfaceStatus;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.retry.ScannerRetryPolicy;
import com.vulnwatch.worker.state.SurfaceStateManager;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.StructuredTaskScope;

/**
 * Replaces ParallelScanner for domain scans using Java 21 StructuredTaskScope.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class DomainScanOrchestrator {

    private final List<Scanner> scanners;
    private final ScannerRetryPolicy retryPolicy;
    private final DomainCircuitBreakerAiEnricher surfaceAiEnricher;
    private final SurfaceStateManager surfaceStateManager;

    /**
     * Runs all scanners in parallel, each followed immediately by AI enrichment.
     *
     * @return OrchestratorResult containing paired engine + AI results aligned by index.
     */
    @SuppressWarnings("preview")
    public OrchestratorResult scan(ScanJob job) {
        log.info("Orchestrator starting [scanId={} scanners={}]", job.scanId(), scanners.size());

        List<EngineResult> engineResults = new ArrayList<>(scanners.size());
        List<AiResult> aiResults = new ArrayList<>(scanners.size());

        try (var scope = new StructuredTaskScope.ShutdownOnFailure()) {

            // Fork virtual threads for each scanner pipeline
            List<StructuredTaskScope.Subtask<SurfaceResult>> subtasks = scanners.stream()
                    .map(scanner -> scope.fork(() -> processSurface(scanner, job)))
                    .toList();

            scope.join();
            scope.throwIfFailed(e -> new RuntimeException("Orchestrator scope encountered fatal system failure [scanId=%s]".formatted(job.scanId()), e));

            // Safely unpack results sequentially in original scanner order (No concurrency overhead)
            for (StructuredTaskScope.Subtask<SurfaceResult> subtask : subtasks) {
                SurfaceResult sr = subtask.get();
                engineResults.add(sr.engineResult());
                aiResults.add(sr.aiResult());
            }

        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            log.error("Orchestrator interrupted [scanId={}]", job.scanId(), e);
            throw new RuntimeException("Orchestrator execution interrupted", e);
        }

        log.info("Orchestrator complete [scanId={} surfaces={} succeeded={}]",
                job.scanId(), engineResults.size(), engineResults.stream().filter(EngineResult::success).count());

        return new OrchestratorResult(engineResults, aiResults);
    }

    private SurfaceResult processSurface(Scanner scanner, ScanJob job) {
        SurfaceType surface = scanner.surfaceType();
        String scanId = job.scanId();

        surfaceStateManager.transition(scanId, surface, SurfaceStatus.SCANNING);
        log.debug("Surface pipeline starting [scanId={} surface={}]", scanId, surface.name());

        try {
            // Step 1: Execute scan via Spring-Retry Proxy
            EngineResult engineResult = retryPolicy.execute(scanner, job);

            if (engineResult == null) {
                log.warn("Surface permanently failed retry attempts [scanId={} surface={}]", scanId, surface.name());
                return new SurfaceResult(EngineResult.failure(surface, "Scanner exhausted all retries"), null);
            }

            // Step 2: Execute AI Enrichment via Circuit-Breaker Proxy
            AiResult aiResult = surfaceAiEnricher.enrich(job, engineResult, surface);
            log.debug("Surface pipeline complete [scanId={} surface={} aiEnriched={}]", scanId, surface.name(), aiResult != null);

            return new SurfaceResult(engineResult, aiResult);

        } catch (Exception e) {
            log.error("Uncaught processing failure in surface pipeline [scanId={} surface={}]: {}", scanId, surface.name(), e.getMessage(), e);
            return new SurfaceResult(EngineResult.failure(surface, "Unexpected pipeline crash: %s".formatted(e.getMessage())), null);
        }
    }


    record SurfaceResult(EngineResult engineResult, AiResult aiResult) {}

    public record OrchestratorResult(List<EngineResult> engineResults, List<AiResult> aiResults) {
        public boolean hasAnySuccess() {
            return engineResults.stream().anyMatch(EngineResult::success);
        }

        public boolean allFailed() {
            return engineResults.stream().noneMatch(EngineResult::success);
        }
    }

    public List<SurfaceType> registeredSurfaces() {
        return scanners.stream()
                .map(Scanner::surfaceType)
                .toList();
    }
}