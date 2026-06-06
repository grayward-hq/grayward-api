package com.vulnwatch.worker.ai.breaker;

import com.vulnwatch.worker.ai.interfaces.AiEnricher;
import com.vulnwatch.worker.enums.AiAvailability;
import com.vulnwatch.worker.enums.FailureReason;
import com.vulnwatch.worker.enums.SurfaceStatus;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.model.AiResult;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.state.SurfaceStateManager;
import io.github.resilience4j.circuitbreaker.CallNotPermittedException;
import io.github.resilience4j.circuitbreaker.CircuitBreaker;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Component;

import java.util.Optional;

/**
 * Per-surface AI enrichment wrapped by a Resilience4j CircuitBreaker.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class DomainCircuitBreakerAiEnricher {

    private final AiEnricher aiEnricher;
    private final CircuitBreaker aiCircuitBreaker;
    private final SurfaceStateManager surfaceStateManager;

    /**
     * Attempts AI enrichment for a single surface result.
     *
     * @param job the originating scan job
     * @param engineResult the scanner output for this surface
     * @param surfaceType  the surface being enriched
     * @return AiResult or null — callers must handle null gracefully
     */
    public AiResult enrich(ScanJob job, EngineResult engineResult, SurfaceType surfaceType) {
        String scanId = job.scanId();
        surfaceStateManager.transition(scanId, surfaceType, SurfaceStatus.ENRICHING);

        return executeEnrichmentWithBreaker(job, engineResult, surfaceType)
                .map(result -> handleSuccess(scanId, surfaceType, result))
                .orElseGet(() -> handleFailure(scanId, surfaceType));
    }

    /**
     * Returns the current AI availability derived from the circuit breaker state.
     */
    public AiAvailability currentAvailability() {
        return switch (aiCircuitBreaker.getState()) {
            case CLOSED, FORCED_OPEN -> AiAvailability.AVAILABLE;
            case OPEN -> AiAvailability.UNAVAILABLE;
            default -> AiAvailability.DEGRADED;
        };
    }

    private Optional<AiResult> executeEnrichmentWithBreaker(ScanJob job, EngineResult engineResult, SurfaceType surfaceType) {
        try {
            AiResult result = aiCircuitBreaker.executeSupplier(() -> aiEnricher.enrich(job, engineResult));
            return Optional.of(result);
        } catch (CallNotPermittedException e) {
            log.warn("AI circuit breaker OPEN — skipping enrichment [scanId={} surface={}]", job.scanId(), surfaceType.name());
        } catch (Exception e) {
            log.error("AI enrichment threw unexpected exception [scanId={} surface={}]: {}", job.scanId(), surfaceType.name(), e.getMessage(), e);
        }
        return Optional.empty();
    }

    private AiResult handleSuccess(String scanId, SurfaceType surfaceType, AiResult result) {
        surfaceStateManager.transition(scanId, surfaceType, SurfaceStatus.SUCCESS);
        log.debug("AI enrichment succeeded [scanId={} surface={} severity={}]", scanId, surfaceType.name(), result.severity());
        return result;
    }

    private AiResult handleFailure(String scanId, SurfaceType surfaceType) {
        surfaceStateManager.transitionFailed(scanId, surfaceType, SurfaceStatus.SUCCESS_NO_AI, FailureReason.AI_ERROR);
        return null;
    }
}