package com.vulnwatch.worker.orchestrator;

import com.vulnwatch.worker.ai.breaker.DomainCircuitBreakerAiEnricher;
import com.vulnwatch.worker.engine.domain.Scanner;
import com.vulnwatch.worker.enums.SurfaceStatus;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.orchestrator.mapper.SurfaceTypeMapper;
import com.vulnwatch.worker.retry.ScannerRetryPolicy;
import com.vulnwatch.worker.state.SurfaceStateManager;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.List;
import java.util.Set;
import java.util.concurrent.StructuredTaskScope;

/**
 * Coordinates and executes parallel target domain vulnerability scans.
 * Dynamically filters pipeline targets based on incoming ScanJob request criteria.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class DomainScanOrchestrator {

    private final List<Scanner> scanners;
    private final ScannerRetryPolicy retryPolicy;
    private final DomainCircuitBreakerAiEnricher surfaceAiEnricher;
    private final SurfaceStateManager surfaceStateManager;
    private final SurfaceTypeMapper surfaceTypeMapper;

    // ThreadLocal ensures that concurrent scan requests don't corrupt each other's active surface lists
    private final ThreadLocal<List<SurfaceType>> activeSurfacesThreadLocal = new ThreadLocal<>();

    @SuppressWarnings("preview")
    public OrchestratorResult scan(ScanJob job) {
        String scanId = job.scanId();
        Set<SurfaceType> requestedSurfaces = surfaceTypeMapper.resolve(job);
        List<Scanner> activeScanners = selectScanners(scanId, requestedSurfaces);

        // Map and preserve the exactly selected surfaces for this thread context
        List<SurfaceType> selected = activeScanners.stream()
                .map(Scanner::surfaceType)
                .toList();
        activeSurfacesThreadLocal.set(selected);

        log.info("Orchestrator starting [scanId={} requestedSurfaces={} targeted={}]",
                scanId, requestedSurfaces.isEmpty() ? "ALL" : requestedSurfaces, activeScanners.size());

        try (var scope = new StructuredTaskScope.ShutdownOnFailure()) {

            // Fork execution flows concurrently into virtual thread paths
            List<StructuredTaskScope.Subtask<SurfaceResult>> subtasks = activeScanners.stream()
                    .map(scanner -> scope.fork(() -> processSurface(scanner, job)))
                    .toList();

            scope.join().throwIfFailed(e -> new RuntimeException(
                    "Orchestrator scope encountered fatal system failure [scanId=%s]".formatted(scanId), e));

            // Map subtask results directly into immutable output lists
            List<EngineResult> engineResults = subtasks.stream().map(s -> s.get().engineResult()).toList();
            List<AiResult> aiResults = subtasks.stream().map(s -> s.get().aiResult()).toList();

            log.info("Orchestrator complete [scanId={} surfaces={} succeeded={}]",
                    scanId, engineResults.size(), engineResults.stream().filter(EngineResult::success).count());

            return new OrchestratorResult(engineResults, aiResults);

        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            log.error("Orchestrator execution path interrupted [scanId={}]", scanId, e);
            throw new RuntimeException("Orchestrator execution interrupted", e);
        } finally {
            // Clean up ThreadLocal allocation to prevent memory leaks in the thread pool
            activeSurfacesThreadLocal.remove();
        }
    }

    /**
     * Replaces the old global fallback method. It now dynamically returns the selected
     * surfaces running on the current executing thread, falling back to all scanners
     * if called outside an active scan context.
     */
    public List<SurfaceType> registeredSurfaces() {
        List<SurfaceType> currentActive = activeSurfacesThreadLocal.get();
        if (currentActive != null) {
            return currentActive;
        }

        // Fallback for initialization checks or if called outside a dynamic scan thread
        return scanners.stream()
                .map(Scanner::surfaceType)
                .toList();
    }

    private List<Scanner> selectScanners(String scanId, Set<SurfaceType> requested) {
        if (requested.isEmpty()) {
            return scanners;
        }

        List<Scanner> targeted = scanners.stream()
                .filter(s -> requested.contains(s.surfaceType()))
                .toList();

        if (targeted.isEmpty()) {
            log.warn("Requested surfaces {} matched no registered scanners [scanId={}] — running all.", requested, scanId);
            return scanners;
        }

        return targeted;
    }

    private SurfaceResult processSurface(Scanner scanner, ScanJob job) {
        SurfaceType surface = scanner.surfaceType();
        String scanId = job.scanId();

        surfaceStateManager.transition(scanId, surface, SurfaceStatus.SCANNING);
        log.debug("Surface pipeline starting [scanId={} surface={}]", scanId, surface.name());

        try {
            EngineResult engineResult = retryPolicy.execute(scanner, job);
            if (engineResult == null) {
                log.warn("Surface permanently failed retry attempts [scanId={} surface={}]", scanId, surface.name());
                return new SurfaceResult(EngineResult.failure(surface, "Scanner exhausted all retries"), null);
            }

            AiResult aiResult = surfaceAiEnricher.enrich(job, engineResult, surface);
            return new SurfaceResult(engineResult, aiResult);

        } catch (Exception e) {
            log.error("Uncaught execution crash in surface pipeline [scanId={} surface={}]: {}", scanId, surface.name(), e.getMessage(), e);
            return new SurfaceResult(EngineResult.failure(surface, "Pipeline crash: %s".formatted(e.getMessage())), null);
        }
    }

    record SurfaceResult(EngineResult engineResult, AiResult aiResult) {}

    public record OrchestratorResult(List<EngineResult> engineResults, List<AiResult> aiResults) {
        public boolean hasAnySuccess() { return engineResults.stream().anyMatch(EngineResult::success); }
        public boolean allFailed() { return engineResults.stream().noneMatch(EngineResult::success); }
    }
}