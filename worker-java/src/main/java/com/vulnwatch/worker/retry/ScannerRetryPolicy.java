package com.vulnwatch.worker.retry;

import com.vulnwatch.worker.engine.domain.Scanner;
import com.vulnwatch.worker.enums.FailureReason;
import com.vulnwatch.worker.enums.SurfaceStatus;
import com.vulnwatch.worker.enums.SurfaceType;
import com.vulnwatch.worker.events.ScannerExhaustedEvent;
import com.vulnwatch.worker.exception.ScannerExecutionException;
import com.vulnwatch.worker.model.EngineResult;
import com.vulnwatch.worker.model.ScanJob;
import com.vulnwatch.worker.state.SurfaceStateManager;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.retry.annotation.Backoff;
import org.springframework.retry.annotation.Recover;
import org.springframework.retry.annotation.Retryable;
import org.springframework.stereotype.Component;

/**
 * Wraps each Scanner.scan() call with Spring Retry @Retryable.
 * Retry behaviour:
 *   - Max 3 attempts per surface
 *   - Exponential backoff: 2s → 4s between attempts
 *   - Each failed attempt transitions the surface to RETRYING in Redis
 *   - On exhaustion: @Recover fires ScannerExhaustedEvent + transitions to PERMANENTLY_FAILED
 * Isolation: DNS retrying does NOT block SSL or HTTP.
 * ScanOrchestrator calls this once per scanner on its own virtual thread,
 * so all three surfaces retry independently in parallel.
 * IMPORTANT: @Retryable requires this bean to be called through the Spring
 * proxy — ScanOrchestrator must inject ScannerRetryPolicy (not call it directly
 * as a plain object) for the annotation to take effect.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class ScannerRetryPolicy {

    private final SurfaceStateManager surfaceStateManager;
    private final ApplicationEventPublisher eventPublisher;

    /**
     * Executes the scanner with up to 3 attempts and exponential backoff.
     * On each failure:
     *   - Increments retry count in Redis
     *   - Transitions surface to RETRYING
     *   - Spring Retry sleeps backoff delay then calls again
     * On success returns the EngineResult immediately.
     * On exhaustion @Recover is called — never returns a result.
     *
     * @throws ScannerExecutionException always — signals to @Recover that all retries failed
     */
    @Retryable(
            retryFor  = ScannerExecutionException.class,
            maxAttempts = 3,
            backoff   = @Backoff(delay = 2000, multiplier = 2)
    )
    public EngineResult execute(Scanner scanner, ScanJob job) {
        SurfaceType surface = scanner.surfaceType();

        log.debug("Scanner executing [scanId={} surface={}]", job.scanId(), surface.name());

        EngineResult result = scanner.scan(job);

        if (!result.success()) {
            // Engine returned a failure result (not an exception).
            // We treat this the same as an exception so Spring Retry picks it up.
            log.warn("Scanner returned failure [scanId={} surface={} reason={}]",
                    job.scanId(), surface.name(), result.errorMessage());

            // Increment retry count and transition to RETRYING in Redis
            int retryCount = surfaceStateManager.incrementRetryCount(job.scanId(), surface);
            surfaceStateManager.transition(job.scanId(), surface, SurfaceStatus.RETRYING);

            throw new ScannerExecutionException(
                    surface,
                    result.errorMessage(),
                    retryCount,
                    FailureReason.SCANNER_ERROR
            );
        }

        return result;
    }

    /**
     * Called by Spring Retry after all 3 attempts are exhausted.
     *
     * Transitions the surface to PERMANENTLY_FAILED in Redis and
     * fires ScannerExhaustedEvent for the DeadLetterQueueHandler.
     *
     * Returns null — ScanOrchestrator checks for null and skips AI enrichment
     * for this surface, allowing the other surfaces to continue unaffected.
     */
    @Recover
    public EngineResult recover(ScannerExecutionException e, Scanner scanner, ScanJob job) {
        SurfaceType surface = scanner.surfaceType();

        log.error("Scanner exhausted all retries [scanId={} surface={} retryCount={} reason={}]",
                job.scanId(), surface.name(), e.getRetryCount(), e.getFailureReason());

        // Final state transition
        surfaceStateManager.transitionFailed(
                job.scanId(),
                surface,
                SurfaceStatus.PERMANENTLY_FAILED,
                e.getFailureReason()
        );

        // Publish event DeadLetterQueueHandler picks this up
        eventPublisher.publishEvent(new ScannerExhaustedEvent(
                job,
                surface,
                e.getRetryCount(),
                e.getFailureReason(),
                e.getMessage()
        ));

        // Return null — ScanOrchestrator handles null gracefully,
        // skips AI enrichment for this surface, continues with others
        return null;
    }



}